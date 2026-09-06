using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Sessions;

/// <summary>
/// Serializes everything that issues, rotates or mass-revokes a session for one identity.
/// <para>
/// It is a two-argument advisory lock in a space of its own, distinct from the one-argument space registration
/// uses, so a sign-in and a registration for the same person can never take each other's lock by accident.
/// </para>
/// </summary>
public interface ISessionLock
{
    /// <summary>
    /// Takes the lock inside the ambient transaction, or answers <see langword="false"/> when the bounded wait
    /// elapses. A refusal is deliberately not an error: the caller answers `429`, which is a status a wrong
    /// password can also reach, rather than one only a valid credential could.
    /// </summary>
    Task<bool> TryAcquireAsync(Guid identityId, CancellationToken cancellationToken);
}

/// <summary>The device description this request came from, derived by the server from the transport.</summary>
public interface IDeviceLabel
{
    string Current { get; }
}

/// <summary>
/// The one place a session is created (IA-REQ-049). Password sign-in uses it, and so will provider sign-in, because
/// the cap has to hold whichever door a person came through.
/// </summary>
public interface ISessionIssuer
{
    Task<UserSession?> IssueAsync(Guid identityId, string correlationId, CancellationToken cancellationToken);
}

public sealed class SessionIssuer(
    IApplicationDbContext context,
    ISessionLock sessionLock,
    IDeviceLabel deviceLabel,
    TimeProvider timeProvider) : ISessionIssuer
{
    /// <summary>How many sessions one identity may hold at once. A **product default**.</summary>
    public const int Cap = 5;

    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public async Task<UserSession?> IssueAsync(Guid identityId, string correlationId, CancellationToken cancellationToken)
    {
        // Nothing below is safe unserialized: two sign-ins reading the same live set would each evict against it
        // and commit, leaving six. The lock is what makes "at most five at any committed instant" true.
        if (!await sessionLock.TryAcquireAsync(identityId, cancellationToken)) return null;

        var now = timeProvider.GetUtcNow();
        var live = await context.UserSessions
            .Where(candidate => candidate.IdentityId == identityId && candidate.RevokedAt == null
                && candidate.IdleExpiresAt > now && candidate.AbsoluteExpiresAt > now)
            .OrderBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        // Evict down to Cap - 1 so the new one fits. More than the cap can exist only from a lowered cap or a
        // defect, and the loop handles that without pretending it cannot happen.
        foreach (var evicted in live.Take(Math.Max(0, live.Count - (Cap - 1))))
        {
            await SessionRevocation.RevokeAsync(context, evicted, now, "evicted", correlationId, cancellationToken);
        }

        var session = UserSession.Create(identityId, now, IdleLifetime, AbsoluteLifetime, deviceLabel.Current);
        var activeTenants = await (from membership in context.TenantMemberships
                                   join tenant in context.Tenants on membership.TenantId equals tenant.Id
                                   where membership.IdentityId == identityId && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                   select tenant.Id).ToListAsync(cancellationToken);
        if (activeTenants.Count == 1) session.SelectTenant(activeTenants[0], now);

        context.UserSessions.Add(session);
        context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, session.Id.Value, "session.created", correlationId, "created"));
        return session;
    }
}

/// <summary>
/// Ending a session, in the one way that is safe to do concurrently.
/// <para>
/// The domain decides the transition and PostgreSQL decides whether this request is the one that performs it: the
/// liveness predicate is evaluated under the row lock, so a session another request already revoked yields zero
/// rows rather than an optimistic conflict, and only the request that actually ended it writes the audit row.
/// </para>
/// </summary>
internal static class SessionRevocation
{
    internal static async Task<bool> RevokeAsync(
        IApplicationDbContext context,
        UserSession session,
        DateTimeOffset now,
        string outcome,
        string correlationId,
        CancellationToken cancellationToken)
    {
        session.Revoke(now);
        var revokedAt = session.RevokedAt!.Value;
        var affected = await context.UserSessions
            .Where(candidate => candidate.Id == session.Id && candidate.RevokedAt == null
                && candidate.IdleExpiresAt > now && candidate.AbsoluteExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.RevokedAt, revokedAt)
                .SetProperty(candidate => candidate.ActiveTenantId, (TenantId?)null)
                // Column-relative, never the version this request loaded: a tenant selection that landed in
                // between bumps the token, and writing an absolute value would roll it back.
                .SetProperty(candidate => candidate.Version, candidate => candidate.Version + 1), cancellationToken);

        await context.ReloadAsync(session, cancellationToken);
        if (affected != 1) return false;

        context.AuditEvents.Add(AuditEvent.CreateSessionEvent(session.IdentityId, session.Id.Value, "session.revoked", correlationId, outcome));
        return true;
    }
}
