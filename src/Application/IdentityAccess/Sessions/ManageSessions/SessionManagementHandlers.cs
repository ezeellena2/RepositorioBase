using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions;

public sealed class ListOwnSessionsQueryHandler(
    IApplicationDbContext context,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<ListOwnSessionsQuery, Result<IReadOnlyList<OwnSessionResponse>>>
{
    public async Task<Result<IReadOnlyList<OwnSessionResponse>>> Handle(ListOwnSessionsQuery request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result<IReadOnlyList<OwnSessionResponse>>.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var currentId = currentSession.SessionId.Value;
        var now = timeProvider.GetUtcNow();

        var live = await context.UserSessions
            .AsNoTracking()
            .Where(session => session.IdentityId == identityId && session.RevokedAt == null
                && session.IdleExpiresAt > now && session.AbsoluteExpiresAt > now)
            .ToListAsync(cancellationToken);

        // The session in use leads, then the most recently seen. Somebody looking for the device they do not
        // recognize should not have to work out which row is the one they are reading this on.
        IReadOnlyList<OwnSessionResponse> ordered = live
            .Select(session => new OwnSessionResponse(
                session.PublicRef.Value,
                session.Id == currentId,
                session.DeviceLabel,
                Truncate(session.CreatedAt),
                Truncate(session.LastSeenAt),
                Truncate(session.IdleExpiresAt)))
            .OrderByDescending(session => session.IsCurrent)
            .ThenByDescending(session => session.LastSeenAt)
            .ThenBy(session => session.SessionRef, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<OwnSessionResponse>>.Success(ordered);
    }

    /// <summary>To the minute. A second-accurate last-seen time is an activity log nobody asked for.</summary>
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);
}

public sealed class RevokeOwnSessionCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRecentIdentityProofStore proofs,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<RevokeOwnSessionCommand, Result>
{
    public async Task<Result> Handle(RevokeOwnSessionCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (!SessionReference.TryFrom(request.SessionRef, out var reference))
            return Result.Failure(IdentityAccessErrors.SessionNotFound());

        var identityId = currentSession.IdentityId.Value;
        var currentId = currentSession.SessionId.Value;
        var correlationId = AuditCorrelation.Current();

        return await transaction.ExecuteAsync(async ct =>
        {
            var target = await context.UserSessions
                .SingleOrDefaultAsync(session => session.IdentityId == identityId && session.PublicRef == reference, ct);

            // A reference that belongs to somebody else is not found, exactly as one that never existed is. The
            // caller learns nothing about whose it might be (IA-REQ-030).
            if (target is null)
            {
                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, currentId.Value, "session.revoke_requested", correlationId, "not_found"));
                await context.SaveChangesAsync(ct);
                return Result.Failure(IdentityAccessErrors.SessionNotFound());
            }

            // Ending the session you are holding is the sign-out contract, and the endpoint answers it there.
            if (target.Id == currentId) return Result.Failure(IdentityAccessErrors.SessionNotFound());

            if (!await proofs.TryConsumeAsync(identityId, currentId, ProofActions.RevokeOneSession, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());

            var now = timeProvider.GetUtcNow();
            if (target.RevokedAt is null && target.IsActiveAt(now))
            {
                await SessionRevocation.RevokeAsync(context, target, now, "revoked_by_owner", correlationId, ct);
                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, currentId.Value, "session.revoke_requested", correlationId, "requested"));
            }

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

public sealed class RevokeOtherSessionsCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRecentIdentityProofStore proofs,
    ISessionLock sessionLock,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<RevokeOtherSessionsCommand, Result>
{
    public async Task<Result> Handle(RevokeOtherSessionsCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var currentId = currentSession.SessionId.Value;
        var correlationId = AuditCorrelation.Current();

        return await transaction.ExecuteAsync(async ct =>
        {
            if (!await proofs.TryConsumeAsync(identityId, currentId, ProofActions.RevokeOtherSessions, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());

            // The same lock a sign-in takes, so a session being issued while this runs is either ended with the
            // rest or created after it, never left behind by a read that raced the insert.
            if (!await sessionLock.TryAcquireAsync(identityId, ct)) return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());

            var now = timeProvider.GetUtcNow();
            var others = await context.UserSessions
                .Where(session => session.IdentityId == identityId && session.Id != currentId && session.RevokedAt == null
                    && session.IdleExpiresAt > now && session.AbsoluteExpiresAt > now)
                .OrderBy(session => session.Id)
                .ToListAsync(ct);

            var revoked = 0;
            foreach (var session in others)
            {
                if (await SessionRevocation.RevokeAsync(context, session, now, "revoked_by_owner", correlationId, ct)) revoked++;
            }

            // One row naming the acting session, and only when something actually ended. A repeat with nothing
            // left to revoke is still success and writes nothing.
            if (revoked > 0)
            {
                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, currentId.Value, "session.revoke_requested", correlationId, "requested"));
            }

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}
