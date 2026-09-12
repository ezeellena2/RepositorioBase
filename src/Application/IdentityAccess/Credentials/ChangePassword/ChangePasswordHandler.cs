using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityCredentialService credentials,
    IRecentIdentityProofStore proofs,
    ISessionIssuer issuer,
    ISessionLock sessionLock,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<ChangePasswordCommand, Result<ReplacedSession>>
{
    public async Task<Result<ReplacedSession>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result<ReplacedSession>.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var actingSession = currentSession.SessionId.Value;
        if (request.NewPassword is null || request.NewPassword.Length > 256)
            return Result<ReplacedSession>.Failure(IdentityAccessErrors.PasswordPolicyFailed(
                new Dictionary<string, ValidationErrorDetail[]>
                {
                    ["newPassword"] = [new ValidationErrorDetail(
                        request.NewPassword is null ? ValidationErrorCodes.Required : ValidationErrorCodes.TooLong,
                        request.NewPassword is null
                            ? new Dictionary<string, int>()
                            : new Dictionary<string, int> { ["max"] = 256 })]
                }));

        return await transaction.ExecuteAsync(async ct =>
        {
            // Taken before anything is read or written, so a sign-in that is validating this identity's old
            // password right now cannot issue its session between this version advance and the revocation below.
            if (!await sessionLock.TryAcquireAsync(identityId, ct))
                return Result<ReplacedSession>.Failure(IdentityAccessErrors.SessionLockUnavailable());

            // The proof is spent first. Validating the policy before asking for authority would tell an unproved
            // caller which passwords this deployment accepts.
            if (!await proofs.TryConsumeAsync(identityId, actingSession, ProofActions.PasswordChange, ct))
                return Result<ReplacedSession>.Failure(IdentityAccessErrors.RecentProofRequired());

            var applied = await credentials.ReplacePasswordAsync(identityId, request.NewPassword, ct);
            if (!applied.Succeeded)
            {
                return Result<ReplacedSession>.Failure(applied.ToApplicationError());
            }

            var now = timeProvider.GetUtcNow();
            await proofs.RecordPasswordChangeAsync(identityId, ct);
            await CredentialSessionEffects.RevokeEveryLiveSessionAsync(context, identityId, actingSession, now, "password_changed", ct);

            // The acting session is rotated rather than kept: the new row inherits no identifier, no antiforgery
            // pair, no proof and no second-factor evidence, which is what "replaced" has to mean to be worth
            // anything (IA-REQ-049).
            var acting = await context.UserSessions.SingleOrDefaultAsync(session => session.Id == actingSession, ct);
            if (acting is not null)
            {
                await SessionRevocation.RevokeAsync(context, acting, now, "rotated", AuditCorrelation.Current(), ct);
            }

            var replacement = await issuer.IssueAsync(identityId, AuditCorrelation.Current(), ct);
            if (replacement is null) return Result<ReplacedSession>.Failure(IdentityAccessErrors.SessionLockUnavailable());

            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, replacement.Id.Value, "identity.password.changed", AuditCorrelation.Current(), "changed"));
            await context.SaveChangesAsync(ct);
            return Result<ReplacedSession>.Success(new ReplacedSession(identityId, replacement.Id.Value));
        }, cancellationToken);
    }
}
