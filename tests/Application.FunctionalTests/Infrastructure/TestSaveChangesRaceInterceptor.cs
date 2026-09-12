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
        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant>()
            .Any(entry => entry.State == EntityState.Added) == true &&
            TestApp.ConsumeForcedRegistrationRollbackAfterPersistedEffects())
        {
            throw new InvalidOperationException("registration rollback after identity persistence");
        }

        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxSecret>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Status == CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxSecretStatus.Consumed) == true &&
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

        // A real competing UPDATE against the invitation row, so the save under test loses its xmin token the
        // way a second request would make it lose it.
        if (eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation>()
            .Any(entry => entry.State == EntityState.Modified) == true &&
            TestApp.ConsumeInvitationConcurrencyConflict())
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(eventData.Context.Database.GetConnectionString())
                .Options;
            await using var competing = new ApplicationDbContext(options);
            await competing.Database.ExecuteSqlRawAsync("UPDATE \"Invitations\" SET \"ExpiresAt\" = \"ExpiresAt\" + INTERVAL '1 second';", cancellationToken);
        }

        var context = eventData.Context;

        // Cookie validation persists activity through an atomic conditional update that never reaches SaveChanges;
        // SessionLivenessRaceInterceptor races that statement. This hook races the explicit state transitions.
        var staleSession = context?.ChangeTracker.Entries<UserSession>()
            .Where(entry => entry.State == EntityState.Modified)
            .Select(entry => entry.Entity)
            .FirstOrDefault();

        if (staleSession is not null && DetectSessionWriteStage(context!) is { } stage)
        {
            if (TestApp.ConsumeConcurrentSessionRevoke(stage))
            {
                await CompeteAsync(context!, staleSession.Id, session => session.Revoke(staleSession.LastSeenAt), cancellationToken);
            }
            else if (TestApp.ConsumeConcurrentSessionSelection(stage) is { } selection)
            {
                await CompeteAsync(context!, staleSession.Id, session => session.SelectTenant(selection.TenantId, staleSession.LastSeenAt), cancellationToken, selection.SuspendMembershipOf);
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

    /// <summary>
    /// Fails an invitation save AFTER it has been written, not before. Throwing in <c>SavingChangesAsync</c> would
    /// abort the statement batch before any row existed, so the rollback assertion would hold vacuously; the whole
    /// point is that rows really landed and the enclosing transaction really took them back.
    /// </summary>
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (TestApp.HasPendingInvitationRollback &&
            eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation>().Any() == true &&
            TestApp.ConsumeForcedInvitationRollbackAfterPersistedEffects())
        {
            throw new InvalidOperationException("invitation rollback after persisted effects");
        }

        // The same shape for a membership's role assignments: the rows are written first and the failure arrives
        // after, so what the enclosing transaction takes back is real state and not a staged change set.
        if (TestApp.HasPendingAssignmentRollback &&
            eventData.Context?.ChangeTracker.Entries<CleanArchitecture.Domain.IdentityAccess.Authorization.MembershipRole>().Any() == true &&
            TestApp.ConsumeForcedAssignmentRollbackAfterPersistedEffects())
        {
            throw new InvalidOperationException("assignment rollback after persisted effects");
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Classifies which session write is about to be persisted so a test can race exactly that step. The
    /// revocation is recognised by its own state transition rather than by a companion audit row, because the
    /// session transition and its audit are persisted as separate statements.
    /// </summary>
    private static SessionWriteStage? DetectSessionWriteStage(DbContext context)
    {
        var session = context.ChangeTracker.Entries<UserSession>().FirstOrDefault(entry => entry.State == EntityState.Modified);
        if (session is null) return null;
        if (session.Property(nameof(UserSession.RevokedAt)).IsModified) return SessionWriteStage.Revocation;
        if (session.Property(nameof(UserSession.ActiveTenantId)).IsModified)
        {
            return session.Entity.ActiveTenantId is null ? SessionWriteStage.TenantClearing : SessionWriteStage.TenantSelection;
        }

        return null;
    }

    private static async Task CompeteAsync(
        DbContext context,
        UserSessionId sessionId,
        Action<UserSession> compete,
        CancellationToken cancellationToken,
        CleanArchitecture.Domain.IdentityAccess.Tenants.TenantId? suspendMembershipOf = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(context.Database.GetConnectionString())
            .Options;
        await using var competingContext = new ApplicationDbContext(options);
        var competingSession = await competingContext.UserSessions.SingleAsync(session => session.Id == sessionId, cancellationToken);
        compete(competingSession);
        if (suspendMembershipOf is { } suspendedTenantId)
        {
            var tenant = await competingContext.Tenants.SingleAsync(candidate => candidate.Id == suspendedTenantId, cancellationToken);
            var membership = await competingContext.TenantMemberships.SingleAsync(
                candidate => candidate.TenantId == suspendedTenantId && candidate.IdentityId == competingSession.IdentityId,
                cancellationToken);
            membership.Suspend(tenant);
        }

        await competingContext.SaveChangesAsync(cancellationToken);
    }
}
