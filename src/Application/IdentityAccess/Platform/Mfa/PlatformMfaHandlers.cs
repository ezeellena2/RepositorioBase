using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

/// <summary>
/// The system roles the Platform tenant carries. They are named rather than inferred so that a membership is
/// always assigned an existing role created by the bootstrap ceremony, and never one invented at activation time
/// — inventing authority at the moment it is granted is exactly what IA-REQ-042 forbids.
/// </summary>
public static class PlatformRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
}

/// <summary>Mints the second factor for the invitee this session belongs to, or restarts an unproved one.</summary>
public sealed class BeginPlatformMfaEnrollmentCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ICurrentSession session,
    ITokenHasher tokenHasher,
    IPlatformMfaVerifier verifier,
    IPlatformRecoveryCodeFactory recoveryCodes,
    TimeProvider timeProvider) : IRequestHandler<BeginPlatformMfaEnrollmentCommand, Result<PlatformMfaEnrollmentDetails>>
{
    public Task<Result<PlatformMfaEnrollmentDetails>> Handle(BeginPlatformMfaEnrollmentCommand request, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var admission = await PlatformMfaGate.AdmitAsync(context, tokenHasher, identities, session, request.Token, now, ct);
            if (admission.IsFailure) return Result<PlatformMfaEnrollmentDetails>.Failure(admission.Error!);

            var identity = (await identities.FindByIdAsync(admission.Value!.IdentityId, ct))!;
            var secret = verifier.Create(identity.Email);
            var issued = recoveryCodes.Create();
            var existing = await PlatformMfaGate.FindEnrollmentAsync(context, admission.Value.IdentityId, ct);

            if (existing is null)
            {
                context.PlatformMfaEnrollments.Add(
                    PlatformMfaEnrollment.Begin(admission.Value.IdentityId, secret.EncryptedSecret, issued.Select(code => code.Hash), now));
            }
            else if (existing.Status == PlatformMfaEnrollmentStatus.Pending)
            {
                // Losing an authenticator before finishing must not need an administrator.
                existing.Restart(secret.EncryptedSecret, issued.Select(code => code.Hash), now);
            }
            else
            {
                // A working factor is never replaced by a request that did not prove the current one.
                return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.InvitationConflict());
            }

            await context.SaveChangesAsync(ct);
            return Result<PlatformMfaEnrollmentDetails>.Success(new PlatformMfaEnrollmentDetails(
                secret.SharedKey,
                secret.ProvisioningUri,
                issued.Select(code => code.Code).ToArray()));
        }, cancellationToken);
}

/// <summary>Accepts a code from the enrolled secret, which is what turns a stored secret into a proved factor.</summary>
public sealed class VerifyPlatformMfaEnrollmentCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ICurrentSession session,
    ITokenHasher tokenHasher,
    IPlatformMfaVerifier verifier,
    ISharedAttemptBudget attempts,
    TimeProvider timeProvider) : IRequestHandler<VerifyPlatformMfaEnrollmentCommand, Result>
{
    public Task<Result> Handle(VerifyPlatformMfaEnrollmentCommand request, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var admission = await PlatformMfaGate.AdmitAsync(context, tokenHasher, identities, session, request.Token, now, ct);
            if (admission.IsFailure) return Result.Failure(admission.Error!);

            // Spent before the code is compared and keyed on the identity, so signing in again to obtain a fresh
            // session buys nothing: the same identity meets the same budget (IA-REQ-041).
            var decision = await attempts.SpendAsync(
                PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(admission.Value!.IdentityId), ct);
            if (PlatformAttemptBudgets.MfaRefusal(decision) is { } refusal) return Result.Failure(refusal);

            var enrollment = await PlatformMfaGate.FindEnrollmentAsync(context, admission.Value.IdentityId, ct);
            if (enrollment is null) return Result.Failure(IdentityAccessErrors.InvalidMfaCode());
            if (!verifier.Verify(enrollment.EncryptedSecret, request.Code, now)) return Result.Failure(IdentityAccessErrors.InvalidMfaCode());

            enrollment.Verify(admission.Value.SessionId, now);
            await context.SaveChangesAsync(ct);
            await attempts.ClearAsync(
                PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(admission.Value.IdentityId), ct);
            return Result.Success();
        }, cancellationToken);
}

/// <summary>
/// The last gate, and the only place a Platform membership is ever created (IA-REQ-041/042).
/// <para>
/// Everything it needs has already been proved by the time it runs — a confirmed identity, a bound invitation, a
/// verified factor — so what it adds is the acknowledgement itself and the single transaction that turns the
/// whole chain into authority: the invitation is consumed, the membership becomes active with a role the
/// bootstrap ceremony created, and the tenant's authorization version moves so the evaluator sees it at once.
/// </para>
/// </summary>
public sealed class AcknowledgePlatformRecoveryCodesCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ICurrentSession session,
    ITokenHasher tokenHasher,
    IPlatformMembershipActivator activator,
    TimeProvider timeProvider) : IRequestHandler<AcknowledgePlatformRecoveryCodesCommand, Result>
{
    public Task<Result> Handle(AcknowledgePlatformRecoveryCodesCommand request, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var admission = await PlatformMfaGate.AdmitAsync(context, tokenHasher, identities, session, request.Token, now, ct);
            if (admission.IsFailure) return Result.Failure(admission.Error!);

            var invitation = admission.Value!.Invitation;
            var identityId = admission.Value.IdentityId;
            var enrollment = await PlatformMfaGate.FindEnrollmentAsync(context, identityId, ct);
            if (enrollment is null || enrollment.Status == PlatformMfaEnrollmentStatus.Pending)
            {
                return Result.Failure(IdentityAccessErrors.InvalidInvitation());
            }

            var platform = await context.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == invitation.TenantId, ct);
            if (platform is null || platform.Type != TenantType.Platform || platform.Status != TenantStatus.Active)
            {
                return Result.Failure(IdentityAccessErrors.InvitationConflict());
            }

            var roleName = invitation.IsOwner ? PlatformRoles.Owner : PlatformRoles.Administrator;

            enrollment.AcknowledgeRecoveryCodes(now);

            var membership = await context.TenantMemberships.SingleOrDefaultAsync(
                candidate => candidate.TenantId == platform.Id && candidate.IdentityId == identityId, ct);
            if (membership is null)
            {
                membership = TenantMembership.CreateInvited(platform, identityId);
                membership.Activate(platform);
                context.TenantMemberships.Add(membership);

                // The role a Platform membership is granted always predates the grant. Creating one here would
                // be inventing authority at the moment of granting it, which IA-REQ-042 forbids.
                if (!await activator.TryAssignSystemRoleAsync(platform, membership, roleName, ct))
                {
                    return Result.Failure(IdentityAccessErrors.InvitationConflict());
                }

                context.AuditEvents.Add(AuditEvent.Create(
                    platform.Id,
                    identityId,
                    "platform.membership.activated",
                    $"platform-activation-{invitation.Id.Value:N}",
                    new Dictionary<string, string>
                    {
                        ["code"] = "platform.membership.activated",
                        ["outcome"] = invitation.IsOwner ? "owner" : "administrator"
                    }));
            }

            invitation.Accept(identityId, now);
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
}

/// <summary>
/// Re-proves the factor for an administrator who already holds Platform authority. It reads the enrollment by
/// identity rather than by invitation, because the invitation was consumed when the membership activated.
/// </summary>
public sealed class StepUpPlatformMfaCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentSession session,
    IPlatformMfaVerifier verifier,
    ISharedAttemptBudget attempts,
    TimeProvider timeProvider) : IRequestHandler<StepUpPlatformMfaCommand, Result>
{
    public Task<Result> Handle(StepUpPlatformMfaCommand request, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            if (session.IsInvalid || session.IdentityId is not { } identityId || session.SessionId is not { } sessionId)
            {
                return Result.Failure(IdentityAccessErrors.InvalidSession());
            }

            // The same budget as enrollment verification, because it is the same secret and the same guess. A
            // limit on one of the two routes would only move the guessing to the other.
            var decision = await attempts.SpendAsync(
                PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(identityId), ct);
            if (PlatformAttemptBudgets.MfaRefusal(decision) is { } refusal) return Result.Failure(refusal);

            var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var enrollment = await PlatformMfaGate.FindEnrollmentAsync(context, identityId, ct);

            // Only a completed enrollment can be stepped up. One that never finished would otherwise be a way to
            // acquire freshness for authority the caller was never granted.
            if (enrollment is null || enrollment.Status != PlatformMfaEnrollmentStatus.Active)
            {
                return Result.Failure(IdentityAccessErrors.InvalidMfaCode());
            }

            if (!verifier.Verify(enrollment.EncryptedSecret, request.Code, now))
            {
                return Result.Failure(IdentityAccessErrors.InvalidMfaCode());
            }

            enrollment.RecordStepUp(sessionId.Value, now);
            await context.SaveChangesAsync(ct);
            await attempts.ClearAsync(PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(identityId), ct);
            return Result.Success();
        }, cancellationToken);
}
