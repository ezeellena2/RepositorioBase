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

public sealed class CreateSessionCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ISessionIssuer issuer) : IRequestHandler<CreateSessionCommand, Result<CreatedSession>>
{
    public async Task<Result<CreatedSession>> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var email)) return await FailedAsync(cancellationToken);
        var account = await identities.ValidateCredentialsAsync(email, request.Password, cancellationToken);
        if (account is null) return await FailedAsync(cancellationToken);

        return await transaction.ExecuteAsync(async ct =>
        {
            var correlationId = AuditCorrelation.Current();

            // Sessions coexist now: a sign-in on one device leaves the others alone, and only the cap ends the
            // oldest when a sixth arrives. Revoking them all was what made a second device impossible (IA-REQ-049).
            var session = await issuer.IssueAsync(account.Id, correlationId, ct);
            if (session is null) return Result<CreatedSession>.Failure(IdentityAccessErrors.SessionLockUnavailable());

            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(account.Id, session.Id.Value, "signin.succeeded", correlationId, "authenticated"));
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
