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
            var now = timeProvider.GetUtcNow();
            if (session is null || !session.IsActiveAt(now))
            {
                return Result.Failure(IdentityAccessErrors.InvalidSession());
            }

            session.Revoke(now);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                session.IdentityId,
                session.Id.Value,
                "session.revoked",
                AuditCorrelation.Current(),
                "revoked"));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}
