using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Sessions;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.OwnCredentials;

/// <summary>
/// What this identity's credential looks like from the outside: whether there is a password at all, and when it
/// last changed. Nothing about the password itself, and nothing about anybody else's.
/// </summary>
public sealed record OwnCredentialsView(bool HasPassword, DateTimeOffset? PasswordUpdatedAt);

/// <summary>
/// Self-service, and about the caller only — there is no subject parameter to point somewhere else, which is what
/// keeps this from becoming a way to ask whether a stranger has a password (IA-REQ-029).
/// </summary>
[Authorize(Permissions.IdentityCredentialsManage, false)]
public sealed record GetOwnCredentialsQuery : IRequest<Result<OwnCredentialsView>>;

public sealed class GetOwnCredentialsQueryHandler(
    IIdentityCredentialService credentials,
    IRecentIdentityProofStore proofs,
    ICurrentSession currentSession) : IRequestHandler<GetOwnCredentialsQuery, Result<OwnCredentialsView>>
{
    public async Task<Result<OwnCredentialsView>> Handle(GetOwnCredentialsQuery request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result<OwnCredentialsView>.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        return Result<OwnCredentialsView>.Success(new OwnCredentialsView(
            await credentials.HasPasswordAsync(identityId, cancellationToken),
            await proofs.PasswordUpdatedAtAsync(identityId, cancellationToken)));
    }
}
