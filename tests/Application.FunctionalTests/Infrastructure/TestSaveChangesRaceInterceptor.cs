using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>Injects real competing PostgreSQL writes or persistence failures only when a functional test arms them.</summary>
public sealed class TestSaveChangesRaceInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (TestApp.ConsumeForcedUnexpectedFailure())
        {
            throw new InvalidOperationException("provider password=must-not-reach-the-client");
        }

        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant>()
            .Any(entry => entry.State == EntityState.Added) == true &&
            TestApp.ConsumeForcedRegistrationRollbackAfterPersistedEffects())
        {
            throw new InvalidOperationException("registration rollback after identity persistence");
        }

        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant>()
            .Any(entry => entry.State == EntityState.Modified) == true &&
            eventData.Context.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxSecret>()
                .Any(entry => entry.State == EntityState.Modified) &&
            TestApp.ConsumeForcedConfirmationRollbackAfterPersistedEffects())
        {
            throw new InvalidOperationException("confirmation rollback after identity activation");
        }

        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Auditing.AuditEvent>()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.EventType == "session.revoked") == true &&
            TestApp.ConsumeSessionRevokePersistenceFailure())
        {
            throw new InvalidOperationException("session revocation rollback after state transition");
        }

        var context = eventData.Context;
        var staleItem = context?.ChangeTracker.Entries<TodoItem>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(entry => entry.Id > 0 && entry.StateIsModified(context));

        if (staleItem is not null && TestApp.ConsumeForcedTodoItemConcurrencyConflict())
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(context!.Database.GetConnectionString())
                .Options;
            await using var competingContext = new ApplicationDbContext(options);
            var competingItem = await competingContext.TodoItems.SingleAsync(item => item.Id == staleItem.Id, cancellationToken);
            competingItem.Note = "concurrent database update";
            await competingContext.SaveChangesAsync(cancellationToken);
        }

        // Races the persistence of a failed sign-in attempt with a competing failure of the same account, which also
        // rotates the Identity concurrency stamp exactly like a second real request would.
        var failingUser = context?.ChangeTracker.Entries<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()
            .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Property(nameof(CleanArchitecture.Infrastructure.Identity.ApplicationUser.AccessFailedCount)).IsModified)?.Entity;
        if (failingUser is not null && TestApp.ConsumeConcurrentFailedAccess())
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(context!.Database.GetConnectionString())
                .Options;
            await using var competingContext = new ApplicationDbContext(options);
            var competingUser = await competingContext.Users.SingleAsync(user => user.Id == failingUser.Id, cancellationToken);
            competingUser.AccessFailedCount++;
            competingUser.ConcurrencyStamp = Guid.NewGuid().ToString();
            await competingContext.SaveChangesAsync(cancellationToken);
        }

        // Cookie validation persists activity through an atomic conditional update that never reaches SaveChanges;
        // SessionLivenessRaceInterceptor races that statement. This hook races the explicit state transitions.
        var staleSession = context?.ChangeTracker.Entries<UserSession>()
            .Where(entry => entry.State is EntityState.Unchanged or EntityState.Modified)
            .Select(entry => entry.Entity)
            .FirstOrDefault();

        if (staleSession is not null && DetectSessionWriteStage(context!) is { } stage)
        {
            if (TestApp.ConsumeConcurrentSessionRevoke(stage))
            {
                await CompeteAsync(context!, staleSession.Id, session => session.Revoke(staleSession.LastSeenAt), cancellationToken);
            }
            else if (stage == SessionWriteStage.TenantClearing && TestApp.ConsumeConcurrentSessionClear())
            {
                await CompeteAsync(context!, staleSession.Id, session => session.ClearActiveTenant(staleSession.LastSeenAt), cancellationToken);
            }
            else if (TestApp.ConsumeConcurrentSessionTouch(stage))
            {
                await CompeteAsync(context!, staleSession.Id, session => session.Touch(staleSession.LastSeenAt.AddSeconds(1)), cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Classifies which session write is about to be persisted so a test can race exactly that step.</summary>
    private static SessionWriteStage? DetectSessionWriteStage(DbContext context)
    {
        var session = context.ChangeTracker.Entries<UserSession>().FirstOrDefault(entry => entry.State is EntityState.Unchanged or EntityState.Modified);
        if (session is null) return null;
        if (context.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Auditing.AuditEvent>().Any(entry => entry.State == EntityState.Added && entry.Entity.EventType == "session.revoked")) return SessionWriteStage.Revocation;
        if (session.State == EntityState.Modified && session.Property(nameof(UserSession.ActiveTenantId)).IsModified)
        {
            return session.Entity.ActiveTenantId is null ? SessionWriteStage.TenantClearing : SessionWriteStage.TenantSelection;
        }

        return null;
    }

    private static async Task CompeteAsync(DbContext context, UserSessionId sessionId, Action<UserSession> compete, CancellationToken cancellationToken)
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

internal static class TodoItemEntryExtensions
{
    internal static bool StateIsModified(this TodoItem entity, DbContext context) =>
        context.Entry(entity).State == EntityState.Modified;
}
