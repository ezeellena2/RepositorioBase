using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.RevokeCurrentSession;

public sealed class RevokeCurrentSessionCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<RevokeCurrentSessionCommand, Result>
{
    public Task<Result> Handle(RevokeCurrentSessionCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.SessionId is null || currentSession.IdentityId is null)
        {
            return Task.FromResult(Result.Failure(IdentityAccessErrors.InvalidSession()));
        }

        return transaction.ExecuteAsync(async ct =>
        {
            var session = await context.UserSessions.SingleOrDefaultAsync(candidate =>
                candidate.Id == currentSession.SessionId.Value &&
                candidate.IdentityId == currentSession.IdentityId.Value, ct);
            if (session is null)
            {
                return Result.Failure(IdentityAccessErrors.InvalidSession());
            }

            for (var attempt = 0; attempt < SessionWriteRetry.Attempts; attempt++)
            {
                // The clock is read per attempt: a reload after a lost update takes real time, and liveness must
                // never be re-decided against the instant the request started.
                var now = timeProvider.GetUtcNow();

                // The committed row is the only authority. A session another request already revoked, or one that
                // expired meanwhile, fails closed and this request audits nothing it did not actually do.
                if (!session.IsActiveAt(now))
                {
                    return Result.Failure(IdentityAccessErrors.InvalidSession());
                }

                session.Revoke(now);
                try
                {
                    // The state transition is persisted on its own, so losing the optimistic update leaves no
                    // half-written audit inside the transaction this boundary is about to commit.
                    await context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await context.ReloadAsync(session, ct);
                    continue;
                }

                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                    session.IdentityId,
                    session.Id.Value,
                    "session.revoked",
                    AuditCorrelation.Current(),
                    "revoked"));
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }

            return Result.Failure(IdentityAccessErrors.SessionConcurrencyConflict());
        }, cancellationToken);
    }
}
