using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;

public sealed class CreateSessionCommandHandler(IApplicationTransaction transaction, IApplicationDbContext context, IIdentityAccountService identities, TimeProvider timeProvider) : IRequestHandler<CreateSessionCommand, Result<CreatedSession>>
{
    public async Task<Result<CreatedSession>> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var email)) return await FailedAsync(cancellationToken);
        var account = await identities.ValidateCredentialsAsync(email, request.Password, cancellationToken);
        if (account is null) return await FailedAsync(cancellationToken);

        return await transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var correlationId = AuditCorrelation.Current();
            var priorSessions = await context.UserSessions
                .Where(candidate => candidate.IdentityId == account.Id && candidate.RevokedAt == null && candidate.IdleExpiresAt > now && candidate.AbsoluteExpiresAt > now)
                // A total order on the rows this request locks: two parallel sign-ins of one identity holding
                // several live sessions would otherwise be able to take the same locks in opposite order, and
                // PostgreSQL would abort one of them as a deadlock — a generic failure for a valid credential.
                .OrderBy(candidate => candidate.Id)
                .ToListAsync(ct);
            foreach (var priorSession in priorSessions.Where(candidate => candidate.IsActiveAt(now)))
            {
                // A fresh sign-in supersedes every other live session of the identity, each one ending audited;
                // expired rows are already dead and stay untouched.
                //
                // The domain decides the transition, but PostgreSQL decides whether this request is the one that
                // performs it: the liveness predicate is evaluated under the row lock, so a session another
                // request revoked meanwhile yields zero rows instead of an optimistic conflict. A parallel
                // sign-in or sign-out therefore never turns a valid credential into an unexpected failure, and
                // this request only audits the supersessions it actually performed (IA-REQ-026/035).
                priorSession.Revoke(now);
                var revokedAt = priorSession.RevokedAt!.Value;
                var superseded = await context.UserSessions
                    .Where(candidate => candidate.Id == priorSession.Id && candidate.RevokedAt == null && candidate.IdleExpiresAt > now && candidate.AbsoluteExpiresAt > now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.RevokedAt, revokedAt)
                        .SetProperty(candidate => candidate.ActiveTenantId, (TenantId?)null)
                        // Column-relative, never the version this request happened to load: a tenant selection
                        // that landed in between bumps the token without revoking, and writing an absolute value
                        // would silently roll it back and let a later stale update believe it still won.
                        .SetProperty(candidate => candidate.Version, candidate => candidate.Version + 1), ct);

                // The transition is persisted (or was already performed by the writer that won); reloading drops
                // the in-memory copy so the SaveChanges below can never resend it as an optimistic update.
                await context.ReloadAsync(priorSession, ct);
                if (superseded == 1)
                {
                    context.AuditEvents.Add(AuditEvent.CreateSessionEvent(account.Id, priorSession.Id.Value, "session.revoked", correlationId, "superseded"));
                }
            }
            var session = UserSession.Create(account.Id, now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
            var activeTenants = await (from membership in context.TenantMemberships
                                       join tenant in context.Tenants on membership.TenantId equals tenant.Id
                                       where membership.IdentityId == account.Id && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                       select tenant.Id).ToListAsync(ct);
            if (activeTenants.Count == 1) session.SelectTenant(activeTenants[0], now);
            context.UserSessions.Add(session);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(account.Id, session.Id.Value, "signin.succeeded", correlationId, "authenticated"));
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(account.Id, session.Id.Value, "session.created", correlationId, "created"));
            await context.SaveChangesAsync(ct);
            return Result<CreatedSession>.Success(new CreatedSession(account.Id, session.Id.Value));
        }, cancellationToken);
    }

    private async Task<Result<CreatedSession>> FailedAsync(CancellationToken cancellationToken)
    {
        await transaction.ExecuteAsync(async ct =>
        {
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(null, null, "signin.failed", AuditCorrelation.Current(), "invalid_credentials"));
            await context.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);
        return Result<CreatedSession>.Failure(new ApplicationError("invalid_session", ApplicationErrorCategory.Authentication));
    }

    private static bool TryNormalize(CreateSessionCommand request, out string email)
    {
        email = string.Empty;
        if (request.Email is null || request.Password is null || request.Email.Length > 256 || request.Password.Length > 256) return false;
        email = request.Email.Trim().ToLowerInvariant();
        return email.Contains('@') && !string.IsNullOrWhiteSpace(request.Password);
    }
}
