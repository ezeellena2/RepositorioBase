using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Outbox;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Identities;

/// <summary>
/// Stops one account, for a reason from a closed set (IA-REQ-054). The reason is recorded in the audit trail and
/// nowhere else: it is a statement about a decision an operator made, not an attribute of the person.
/// <para>
/// <c>ExpectedStatus</c> is a precondition rather than a hint. Two operators reading the same directory page and
/// acting on it cannot both believe they were the one who moved the account.
/// </para>
/// </summary>
[Authorize(Permissions.PlatformIdentitiesManage, true)]
public sealed record SuspendIdentityCommand(Guid IdentityId, IdentitySuspensionReason Reason, IdentityAccountStatus ExpectedStatus) : IRequest<Result>;

/// <summary>
/// Lifts a suspension, into the state that preceded it.
/// <para>
/// <c>AcknowledgeSelfDeactivation</c> is what stops an operator believing they restored somebody's access when
/// they did not. If the account was parked by the person before it was suspended, lifting the suspension returns
/// it to that — their decision is not the operator's to undo — and the operator has to say they know that.
/// </para>
/// </summary>
[Authorize(Permissions.PlatformIdentitiesManage, true)]
public sealed record ReactivateIdentityCommand(Guid IdentityId, IdentityAccountStatus ExpectedStatus, bool AcknowledgeSelfDeactivation) : IRequest<Result>;

public sealed class SuspendIdentityCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa,
    IIdentityLifecycleStore lifecycle,
    IRecentIdentityProofStore proofs,
    IPlatformMembershipActivator platformMemberships,
    ISessionLock sessionLock,
    TimeProvider timeProvider) : IRequestHandler<SuspendIdentityCommand, Result>
{
    public async Task<Result> Handle(SuspendIdentityCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken)) return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        if (request.IdentityId == Guid.Empty || !Enum.IsDefined(request.Reason)) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        // Neither terminal nor already-stopped is a state a suspension can start from, and saying so is not a
        // disclosure: the caller supplied both halves of the claim.
        if (request.ExpectedStatus is IdentityAccountStatus.Closed or IdentityAccountStatus.AdministrativelySuspended)
            return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        return await transaction.ExecuteAsync(async ct =>
        {
            if (await PlatformContext.ResolveAsync(context, currentTenant, ct) is null)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var state = await lifecycle.FindAsync(request.IdentityId, ct);
            if (state is null) return Result.Failure(IdentityAccessErrors.IdentityNotFound());
            if (state.Status != request.ExpectedStatus) return Result.Failure(IdentityAccessErrors.IdentityConcurrencyConflict());

            if (await IdentityLifecycleEffects.WouldOrphanThePlatformAsync(context, platformMemberships, request.IdentityId, ct))
                return Result.Failure(IdentityAccessErrors.PlatformLastOwner());

            // The subject's own lock, not the operator's: what is being serialized against is a sign-in by the
            // person being stopped, which would otherwise issue a session after the revocation below has run.
            if (!await sessionLock.TryAcquireAsync(request.IdentityId, ct))
                return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());

            var now = timeProvider.GetUtcNow();
            if (!await lifecycle.TrySuspendAsync(request.IdentityId, request.ExpectedStatus, ct))
                return Result.Failure(IdentityAccessErrors.IdentityConcurrencyConflict());

            await IdentityLifecycleEffects.ApplyDisableAsync(context, proofs, request.IdentityId, now, "administratively_suspended", ct);
            context.AuditEvents.Add(PlatformIdentityAudit.Changed(request.IdentityId, "administratively_suspended", request.Reason.ToString()));
            context.OutboxMessages.Add(OutboxMessage.Create(
                PlatformIdentityAudit.NoticeMessageType,
                JsonSerializer.Serialize(new PlatformIdentityAudit.NoticeEnvelope(request.IdentityId, "administratively_suspended")),
                now));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

public sealed class ReactivateIdentityCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa,
    IIdentityLifecycleStore lifecycle,
    TimeProvider timeProvider) : IRequestHandler<ReactivateIdentityCommand, Result>
{
    public async Task<Result> Handle(ReactivateIdentityCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken)) return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        if (request.IdentityId == Guid.Empty) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        return await transaction.ExecuteAsync(async ct =>
        {
            if (await PlatformContext.ResolveAsync(context, currentTenant, ct) is null)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var state = await lifecycle.FindAsync(request.IdentityId, ct);
            if (state is null) return Result.Failure(IdentityAccessErrors.IdentityNotFound());

            // Asked before the expected-state comparison, because "there is no way back from this" is the more
            // useful thing to be told than "the state moved" — and it is true whatever the caller expected.
            if (state.Status == IdentityAccountStatus.Closed)
                return Result.Failure(IdentityAccessErrors.IdentityReactivationUnavailable());

            if (state.Status != request.ExpectedStatus) return Result.Failure(IdentityAccessErrors.IdentityConcurrencyConflict());
            if (state.Status != IdentityAccountStatus.AdministrativelySuspended)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            // Where the account was when the suspension interrupted it, not `Active` unconditionally. An account
            // with nothing remembered was suspended before this was recorded, and `Active` is the only state a
            // suspension could have started from then.
            var restored = state.StatusBeforeSuspension ?? IdentityAccountStatus.Active;
            if (restored is IdentityAccountStatus.AdministrativelySuspended or IdentityAccountStatus.Closed)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var overSelfDeactivation = restored == IdentityAccountStatus.SelfDeactivated;
            if (overSelfDeactivation && !request.AcknowledgeSelfDeactivation)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            if (!await lifecycle.TryLiftSuspensionAsync(request.IdentityId, restored, ct))
                return Result.Failure(IdentityAccessErrors.IdentityConcurrencyConflict());

            // No sessions and no proofs are restored. They were revoked when the account was stopped, and giving
            // them back would make a suspension something a person can wait out.
            context.AuditEvents.Add(PlatformIdentityAudit.Changed(
                request.IdentityId, overSelfDeactivation ? "reactivated_over_self_deactivation" : "reactivated", null));
            context.OutboxMessages.Add(OutboxMessage.Create(
                PlatformIdentityAudit.NoticeMessageType,
                JsonSerializer.Serialize(new PlatformIdentityAudit.NoticeEnvelope(request.IdentityId, "reactivated")),
                timeProvider.GetUtcNow()));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

/// <summary>The one shape both operator transitions are recorded in.</summary>
internal static class PlatformIdentityAudit
{
    internal const string NoticeMessageType = "identity.lifecycle.notice.requested";

    internal sealed record NoticeEnvelope(Guid IdentityId, string Outcome);

    internal static AuditEvent Changed(Guid identityId, string outcome, string? reason) =>
        AuditEvent.CreateSessionEvent(identityId, null, "identity.lifecycle.changed", AuditCorrelation.Current(), outcome, reason);
}
