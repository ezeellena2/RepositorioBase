using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Sessions;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;

public sealed class ReauthenticateCommandHandler(
    IApplicationTransaction transaction,
    IIdentityAccountService identities,
    IRecentIdentityProofStore proofs,
    ICurrentSession currentSession) : IRequestHandler<ReauthenticateCommand, Result>
{
    public async Task<Result> Handle(ReauthenticateCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        // An unknown action and a wrong password answer the same way. Distinguishing them would let a caller
        // enumerate what this system considers sensitive, which is not something they were shown.
        if (request.Action is null || !ProofActions.All.Contains(request.Action) || request.Password is null || request.Password.Length > 256)
            return Result.Failure(IdentityAccessErrors.InvalidCredentialProof());

        var identityId = currentSession.IdentityId.Value;
        var identity = await identities.FindByIdAsync(identityId, cancellationToken);
        if (identity is null) return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (!identity.IsActive) return Result.Failure(IdentityAccessErrors.EmailConfirmationRequired());

        var validated = await identities.ValidateCredentialsAsync(identity.Email, request.Password, cancellationToken);
        if (validated is null || validated.Id != identityId) return Result.Failure(IdentityAccessErrors.InvalidCredentialProof());

        return await transaction.ExecuteAsync(async ct =>
        {
            await proofs.IssueAsync(identityId, currentSession.SessionId.Value, request.Action, RecentIdentityProofMethod.Password, null, ct);
            return Result.Success();
        }, cancellationToken);
    }
}
