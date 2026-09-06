using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Credentials;

/// <summary>
/// What a credential change does to the sessions that credential opened (IA-REQ-049).
/// <para>
/// It is one place rather than two because the two callers differ only in which session survives: a reset keeps
/// none, and a change keeps the one that asked — as a new row, because a rotated session inherits nothing.
/// </para>
/// </summary>
internal static class CredentialSessionEffects
{
    internal static async Task<int> RevokeEveryLiveSessionAsync(
        IApplicationDbContext context,
        Guid identityId,
        UserSessionId? except,
        DateTimeOffset now,
        string outcome,
        CancellationToken cancellationToken)
    {
        var live = await context.UserSessions
            .Where(session => session.IdentityId == identityId && session.RevokedAt == null
                && session.IdleExpiresAt > now && session.AbsoluteExpiresAt > now)
            .OrderBy(session => session.Id)
            .ToListAsync(cancellationToken);

        var revoked = 0;
        foreach (var session in live.Where(session => except is null || session.Id != except.Value))
        {
            if (await SessionRevocation.RevokeAsync(context, session, now, outcome, AuditCorrelation.Current(), cancellationToken)) revoked++;
        }

        return revoked;
    }
}
