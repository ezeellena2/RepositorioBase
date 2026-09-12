using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using Microsoft.EntityFrameworkCore;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

/// <summary>
/// Replaces a lost second factor against one unspent recovery code (IA-REQ-041, C6).
/// <para>
/// The order of the gates is the contract. The attempt budget is spent before the code is compared, so guessing
/// costs the same here as it does on `/verify`; the proof is spent before anything is read, so an unproved caller
/// learns nothing about which codes exist; and the replacement is written as one conditional update on the single
/// enrollment row, so two recoveries leave one factor.
/// </para>
/// </summary>
public sealed class RecoverPlatformMfaCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ICurrentSession session,
    IRecentIdentityProofStore proofs,
    IPlatformMfaVerifier verifier,
    IPlatformRecoveryCodeFactory recoveryCodes,
    ISharedAttemptBudget attempts,
    TimeProvider timeProvider) : IRequestHandler<RecoverPlatformMfaCommand, Result<PlatformMfaEnrollmentDetails>>
{
    public async Task<Result<PlatformMfaEnrollmentDetails>> Handle(RecoverPlatformMfaCommand request, CancellationToken cancellationToken)
    {
        if (session.IsInvalid || session.IdentityId is not { } identityId || session.SessionId is not { } sessionId)
            return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.InvalidSession());

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                if (await identities.FindByIdAsync(identityId, ct) is not { Status: IdentityAccountStatus.Active })
                    return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.InvalidSession());

                // Spent before the code is compared and keyed on the identity, so signing in again to obtain a
                // fresh session buys nothing: the same identity meets the same budget (IA-REQ-041).
                var decision = await attempts.SpendAsync(
                    PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(identityId), ct);
                if (PlatformAttemptBudgets.MfaRefusal(decision) is { } refusal)
                    return Result<PlatformMfaEnrollmentDetails>.Failure(refusal);

                // The proof is spent before anything is read. Asking for authority after looking would let an
                // unproved caller learn whether an enrollment exists at all.
                if (!await proofs.TryConsumeAsync(identityId, sessionId, ProofActions.PlatformMfaRecover, ct))
                    return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.RecentProofRequired());

                var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
                var enrollment = await PlatformMfaGate.FindEnrollmentAsync(context, identityId, ct);
                if (enrollment is null)
                    return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.InvalidRecoveryCode());

                var identity = (await identities.FindByIdAsync(identityId, ct))!;
                var secret = verifier.Create(identity.Email);
                var issued = recoveryCodes.Create();

                // A wrong code, a spent one, a code from a set this recovery already replaced, and an enrollment
                // that never completed are one answer. Which of them it was is state the caller was never shown.
                if (!enrollment.TryRecover(recoveryCodes.Hash(request.RecoveryCode), secret.EncryptedSecret, issued.Select(code => code.Hash), now))
                {
                    context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                        identityId, sessionId.Value, "platform.mfa.recovered", AuditCorrelation.Current(), "code_reused"));
                    await context.SaveChangesAsync(ct);
                    return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.InvalidRecoveryCode());
                }

                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                    identityId, sessionId.Value, "platform.mfa.recovered", AuditCorrelation.Current(), "factor_replaced"));
                await context.SaveChangesAsync(ct);
                await attempts.ClearAsync(PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(identityId), ct);

                // Shown exactly once, like the enrollment that produced the factor being replaced. Nothing here
                // is readable again: the secret is stored encrypted and the codes only as hashes.
                return Result<PlatformMfaEnrollmentDetails>.Success(new PlatformMfaEnrollmentDetails(
                    secret.SharedKey,
                    secret.ProvisioningUri,
                    issued.Select(code => code.Code).ToArray()));
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The single enrollment row moved under this write, which is the second of two recoveries reaching
            // it. Answering the conflict is what stops a caller walking away with a secret that is already stale.
            return Result<PlatformMfaEnrollmentDetails>.Failure(IdentityAccessErrors.PlatformMfaConcurrencyConflict());
        }
    }
}
