using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// Parking your own account (IA-REQ-054).
/// <para>
/// What this does and does not touch is the contract: every session goes, every proof stops meaning anything, and
/// every token-bearing intent over this identity's own credentials is terminalized — while memberships and
/// invitations addressed to this person's mailbox are left exactly as they were, because coming back should find
/// the same organizations, not a rebuilt approximation of them.
/// </para>
/// </summary>
public sealed class DeactivateAccountCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    IRecentIdentityProofStore proofs,
    IRoleAdministrationStore roles,
    IPlatformMembershipActivator platformMemberships,
    ISessionLock sessionLock,
    IRoleAuthorityLock authorityLock,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<DeactivateAccountCommand, Result>
{
    /// <summary>The notice a person gets when their account is parked. It carries no token and no address.</summary>
    public const string NoticeMessageType = "identity.lifecycle.notice.requested";

    public sealed record NoticeEnvelope(Guid IdentityId, string Outcome);

    public async Task<Result> Handle(DeactivateAccountCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var actingSession = currentSession.SessionId.Value;

        return await transaction.ExecuteAsync(async ct =>
        {
            // Taken first, and for the reason a password change takes it: revocation reaches the sessions that
            // exist when it runs, so a sign-in issuing one afterwards would escape it entirely. Held to commit,
            // it leaves two orderings and no third (IA-REQ-049).
            if (!await sessionLock.TryAcquireAsync(identityId, ct))
                return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());

            // Read after the wait. A reading taken before queueing would be earlier than a session a competing
            // sign-in committed while this was waiting, and revoking something before it existed is not a thing
            // that can have happened.
            var now = timeProvider.GetUtcNow();

            if (!await proofs.TryConsumeAsync(identityId, actingSession, ProofActions.AccountDeactivate, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());

            if (await OrphansAnOrganizationAsync(identityId, ct))
                return Result.Failure(IdentityAccessErrors.LastAdministratorRequired());

            if (await OrphansThePlatformAsync(identityId, ct))
                return Result.Failure(IdentityAccessErrors.PlatformLastOwner());

            // The one conditional statement. A second request attempting the same transition finds its own
            // precondition gone and is told so, rather than both of them believing they parked the account.
            if (!await identities.TryTransitionAsync(identityId, IdentityAccountStatus.Active, IdentityAccountStatus.SelfDeactivated, ct))
                return Result.Failure(IdentityAccessErrors.IdentityConcurrencyConflict());

            await IdentityLifecycleEffects.ApplyDisableAsync(context, proofs, identityId, now, "account_deactivated", ct);

            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                identityId, actingSession.Value, "identity.lifecycle.changed", AuditCorrelation.Current(), "self_deactivated"));
            context.OutboxMessages.Add(OutboxMessage.Create(
                NoticeMessageType, JsonSerializer.Serialize(new NoticeEnvelope(identityId, "self_deactivated")), now));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// The same question an operator's suspension asks, asked from the same place. Both routes stop an account
    /// and both can empty an organization; keeping one copy of the question is what stops the two answers
    /// drifting apart.
    /// </summary>
    private Task<bool> OrphansAnOrganizationAsync(Guid identityId, CancellationToken cancellationToken) =>
        IdentityLifecycleEffects.WouldOrphanAnOrganizationAsync(context, roles, authorityLock, identityId, cancellationToken);

    private Task<bool> OrphansThePlatformAsync(Guid identityId, CancellationToken cancellationToken) =>
        IdentityLifecycleEffects.WouldOrphanThePlatformAsync(context, platformMemberships, identityId, cancellationToken);

}

/// <summary>
/// Asking for the way back. It writes at most one thing and says nothing about whether it did (IA-REQ-029).
/// </summary>
public sealed class RequestAccountReactivationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<RequestAccountReactivationCommand, Result>
{
    public const string MessageType = "identity.reactivation.requested";

    /// <summary>How long a mailed reactivation ticket lives. A **product default**, accepted with C6.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Named only by its identifier: the payload holds no address and no ticket.</summary>
    public sealed record Envelope(Guid RequestId);

    public async Task<Result> Handle(RequestAccountReactivationCommand request, CancellationToken cancellationToken)
    {
        if (request.Email is null || request.Email.Length > 256) return Result.Success();
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return Result.Success();

        return await transaction.ExecuteAsync(async ct =>
        {
            var identity = await identities.FindByEmailAsync(email, ct);

            // Three ways to have nothing to do — no such address, an account that is not parked, and one whose
            // state has no self-service way back — and one answer for all of them. A caller cannot tell which
            // case they are in, which is the point (IA-REQ-029).
            if (identity is null || identity.Status != IdentityAccountStatus.SelfDeactivated) return Result.Success();

            var now = timeProvider.GetUtcNow();
            await SupersedePendingAsync(identity.Id, now, ct);

            var rawToken = tokens.Generate();
            var reactivation = AccountReactivationRequest.Issue(identity.Id, tokenHasher.Of(rawToken), now, Lifetime);
            var outbox = OutboxMessage.Create(MessageType, JsonSerializer.Serialize(new Envelope(reactivation.Id)), now);
            context.AccountReactivationRequests.Add(reactivation);
            context.OutboxMessages.Add(outbox);
            context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), reactivation.ExpiresAt));
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                identity.Id, null, "identity.reactivation.requested", AuditCorrelation.Current(), "accepted"));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>Reissuing replaces rather than adds, so asking twice never leaves two tickets that work.</summary>
    private async Task SupersedePendingAsync(Guid identityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await context.AccountReactivationRequests
            .Where(candidate => candidate.IdentityId == identityId && candidate.Status == AccountReactivationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var request in pending)
        {
            request.Supersede(now);
            await CredentialEnvelopes.TerminalizeAsync(
                context, MessageType, JsonSerializer.Serialize(new Envelope(request.Id)), "superseded", now, cancellationToken);
        }
    }
}

/// <summary>
/// Spending the ticket. Two things are required and neither is sufficient: the ticket, which proves control of
/// the mailbox, and the password, which proves a current authenticator (IA-REQ-054, withdrawal E1).
/// </summary>
public sealed class ReactivateAccountCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ITokenHasher tokenHasher,
    TimeProvider timeProvider) : IRequestHandler<ReactivateAccountCommand, Result>
{
    public async Task<Result> Handle(ReactivateAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ReactivationToken is null || request.Password is null || request.Password.Length > 256)
            return Result.Failure(IdentityAccessErrors.InvalidReactivation());

        var hash = tokenHasher.Of(request.ReactivationToken);
        if (hash.IsEmpty) return Result.Failure(IdentityAccessErrors.InvalidReactivation());

        return await transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var ticket = await context.AccountReactivationRequests.SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, ct);

            // Unknown, spent, superseded and expired are one answer, and the state each of them was in is state
            // the holder of a dead ticket was never shown.
            if (ticket is null || !ticket.IsPendingAt(now)) return Result.Failure(IdentityAccessErrors.InvalidReactivation());

            // The password is checked before the ticket is spent, so one wrong answer does not burn the way back
            // for the person who does hold the mailbox. Guessing is bounded by the same lockout and the same rate
            // limit the front door has, not by destroying the ticket (IA-REQ-019).
            if (!await identities.VerifyPasswordAsync(ticket.IdentityId, request.Password, ct))
                return Result.Failure(IdentityAccessErrors.InvalidReactivation());

            // Only a parked account comes back this way, and the conditional update is what enforces it: an
            // administrative suspension that landed while this ticket was in flight fails this precondition, so
            // the ticket cannot lift it. The refusal is worded like every other one here.
            if (!await identities.TryTransitionAsync(ticket.IdentityId, IdentityAccountStatus.SelfDeactivated, IdentityAccountStatus.Active, ct))
                return Result.Failure(IdentityAccessErrors.InvalidReactivation());

            ticket.Consume(now);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                ticket.IdentityId, null, "identity.lifecycle.changed", AuditCorrelation.Current(), "reactivated"));
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                ticket.IdentityId, null, "identity.reactivation.completed", AuditCorrelation.Current(), "reactivated"));
            await context.SaveChangesAsync(ct);

            // Deliberately no session. Holding a mailed ticket and a password is how you get your account back;
            // signing in is a separate thing you then do, through the front door, like everybody else.
            return Result.Success();
        }, cancellationToken);
    }
}
