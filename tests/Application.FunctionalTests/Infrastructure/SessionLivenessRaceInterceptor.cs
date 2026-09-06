using System.Data.Common;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Races the atomic session updates issued outside SaveChanges — the liveness touch during cookie validation and
/// the supersession of prior sessions during sign-in — with a competing PostgreSQL writer, so tests can prove
/// that the committed row decides the outcome of the request.
/// </summary>
public sealed class SessionLivenessRaceInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ApplicationDbContext context &&
            command.CommandText.StartsWith("UPDATE \"UserSessions\"", StringComparison.Ordinal))
        {
            // The cap eviction is the only statement that is both an ExecuteUpdate — which EF emits with a table
            // alias, unlike a SaveChanges update — and one that assigns ActiveTenantId, which the liveness touch
            // never mentions. Matching both keeps a revocation or tenant selection out of this branch.
            if (IsSupersession(command.CommandText))
            {
                if (TestApp.ConsumeConcurrentSessionRevoke(SessionWriteStage.Eviction))
                {
                    await CompeteWithSignOutAsync(context, cancellationToken);
                }
                else if (TestApp.ConsumeConcurrentSessionSelection(SessionWriteStage.Eviction) is { } selection)
                {
                    // A tenant selection rotates the concurrency token without revoking, so it is the writer that
                    // proves whether the eviction advances the token or overwrites it with a stale value.
                    await CompeteWithSelectionAsync(context, selection.TenantId, cancellationToken);
                }

                return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
            }

            var tracked = context.ChangeTracker.Entries<UserSession>().Select(entry => entry.Entity).FirstOrDefault();
            if (tracked is not null)
            {
                if (TestApp.ConsumeSessionValidationConcurrentRevoke())
                {
                    await CompeteAsync(context, tracked.Id, session => session.Revoke(tracked.LastSeenAt), cancellationToken);
                }
                else if (TestApp.ConsumeConcurrentSessionTouch(SessionWriteStage.Validation))
                {
                    await CompeteAsync(context, tracked.Id, session => session.Touch(tracked.LastSeenAt.AddSeconds(1)), cancellationToken);
                }
            }
        }

        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>A parallel tenant selection on the session the cap is about to evict.</summary>
    private static async Task CompeteWithSelectionAsync(ApplicationDbContext context, CleanArchitecture.Domain.IdentityAccess.Tenants.TenantId tenantId, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(context.Database.GetConnectionString())
            .Options;
        await using var competingContext = new ApplicationDbContext(options);
        // The oldest live row is the one the eviction is writing, and it is the only one a competing write can
        // race. Sessions coexist now, so "the live session" is no longer a thing there is one of (IA-REQ-049).
        var live = await competingContext.UserSessions
            .Where(session => session.RevokedAt == null)
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .FirstAsync(cancellationToken);
        live.SelectTenant(tenantId, live.LastSeenAt);
        await competingContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsSupersession(string commandText) =>
        commandText.StartsWith("UPDATE \"UserSessions\" AS ", StringComparison.Ordinal) &&
        commandText.Contains("\"ActiveTenantId\"", StringComparison.Ordinal);

    /// <summary>A parallel sign-out of exactly the session the cap is about to evict.</summary>
    private static async Task CompeteWithSignOutAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(context.Database.GetConnectionString())
            .Options;
        await using var competingContext = new ApplicationDbContext(options);
        var oldest = await competingContext.UserSessions
            .Where(session => session.RevokedAt == null)
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .FirstAsync(cancellationToken);
        oldest.Revoke(oldest.LastSeenAt);
        await competingContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CompeteAsync(ApplicationDbContext context, UserSessionId sessionId, Action<UserSession> compete, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(context.Database.GetConnectionString())
            .Options;
        await using var competingContext = new ApplicationDbContext(options);
        var competingSession = await competingContext.UserSessions.SingleAsync(session => session.Id == sessionId, cancellationToken);
        compete(competingSession);
        await competingContext.SaveChangesAsync(cancellationToken);
    }
}
