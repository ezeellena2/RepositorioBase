using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;

/// <summary>
/// Reissues the pending owner invitation, or does nothing and says so in exactly the same way (IA-REQ-040).
/// <para>
/// Every state a caller must not learn about answers with the same neutral success: no bootstrap configured, no
/// invitation, an owner already activated, an invitation still in flight, a configuration that no longer matches.
/// Only two things break that silence, and both are decided from the request rather than from state — a missing
/// antiforgery pair, which the endpoint refuses, and an exhausted limit, which is the one answer that has to be
/// distinguishable so a client can back off.
/// </para>
/// </summary>
public sealed class RecoverPendingPlatformOwnerInvitationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IPlatformBootstrapOptions options,
    RecoverPendingPlatformOwnerInvitationValidator validator,
    IPlatformBootstrapRecoveryRateLimiter rateLimiter,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<RecoverPendingPlatformOwnerInvitationCommand, Result>
{
    public async Task<Result> Handle(RecoverPendingPlatformOwnerInvitationCommand request, CancellationToken cancellationToken)
    {
        var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());

        // Read the pending owner before the limit, so the limit can be keyed to it. Reading it costs the same
        // whether one exists or not, and the answer never leaves this method except as a key.
        var pending = await context.PlatformAdminInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(invitation => invitation.IsOwner && invitation.Status == PlatformAdminInvitationStatus.Pending, cancellationToken);

        var decision = await rateLimiter.TryAcquireAsync(pending?.Id.Value, cancellationToken);
        if (PlatformAttemptBudgets.RecoveryRefusal(decision) is { } refusal) return Result.Failure(refusal);

        if (pending is null) return Result.Success();

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var invitation = await context.PlatformAdminInvitations
                    .SingleOrDefaultAsync(candidate => candidate.Id == pending.Id, ct);
                if (invitation is null) return Result.Success();

                // Delivery state comes from the outbox, read at the moment the decision is made rather than
                // whenever a background job last wrote it down.
                await PlatformInvitationDelivery.SynchronizeDeliveryAsync(context, invitation, now, ct);

                var decision = validator.Decide(invitation, options.OwnerEmail, now);
                if (!decision.IsEligible) return Result.Success();

                var superseded = invitation.TokenHash;
                var minted = PlatformInvitationDelivery.Mint(tokens, tokenHasher);
                invitation.Reissue(minted.Hash, now, now.Add(BootstrapPlatformOwner.InvitationWindow));

                // The superseded envelope is retired in the same transaction that rotates the token, so there is
                // never an instant with two deliverable tokens for one invitation.
                await PlatformInvitationDelivery.RetireAsync(context, superseded, "bootstrap_recovery_rotated", now, ct);
                PlatformInvitationDelivery.Deliver(context, secretWriter, invitation, minted, now, now.Add(BootstrapPlatformOwner.InvitationWindow));

                context.AuditEvents.Add(AuditEvent.Create(
                    invitation.TenantId,
                    null,
                    "platform.bootstrap.recovered",
                    $"platform-recovery-{invitation.Id.Value:N}",
                    new Dictionary<string, string>
                    {
                        ["code"] = "platform.bootstrap.recovered",
                        ["outcome"] = "owner_invitation_reissued"
                    }));

                await context.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent recovery already produced the current invitation and effect set. There is exactly one,
            // which is what the requirement asks for, and this caller gets the same neutral answer as the winner.
            return Result.Success();
        }
    }
}
