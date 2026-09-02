using System.Data.Common;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Races the atomic session liveness update issued during cookie validation with a competing PostgreSQL
/// writer, so tests can prove that the committed row decides the outcome of the request.
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
