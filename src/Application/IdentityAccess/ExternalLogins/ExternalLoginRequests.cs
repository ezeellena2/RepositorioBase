using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.ExternalLogins;

/// <summary>
/// Where the browser must go next, and which handoff it is. The URI is our own origin — the middleware issues the
/// provider challenge — and the identifier is for the endpoint to seal into a cookie, never for the response body.
/// </summary>
public sealed record ExternalAuthorizationHandoff(Guid HandoffId, string AuthorizationRequestUri);

/// <summary>Public: somebody signing in with a provider has no session yet, which is the point of it.</summary>
public sealed record StartExternalLoginCommand(string Provider) : IRequest<Result<ExternalAuthorizationHandoff>>, IPublicRequest;

/// <summary>
/// Linking is explicit and always was. It needs a confirmed, authenticated identity, a live proof, and consent
/// stated in the request — none of which an unattended sign-in can supply, which is the difference BR-ID-005/006
/// is about (amendment A2).
/// </summary>
[Authorize(Permissions.IdentityExternalManage, false)]
public sealed record StartExternalLinkCommand(string Provider, bool Consent) : IRequest<Result<ExternalAuthorizationHandoff>>;

[Authorize(Permissions.IdentityCredentialsManage, false)]
public sealed record StartExternalProofCommand(string Provider, string Action) : IRequest<Result<ExternalAuthorizationHandoff>>;

public sealed record CompletedExternalLogin(Guid IdentityId, Guid SessionId);

public sealed record CompleteExternalLoginCommand : IRequest<Result<CompletedExternalLogin>>, IPublicRequest;

[Authorize(Permissions.IdentityExternalManage, false)]
public sealed record CompleteExternalLinkCommand : IRequest<Result>;

[Authorize(Permissions.IdentityCredentialsManage, false)]
public sealed record CompleteExternalProofCommand : IRequest<Result>;

[Authorize(Permissions.IdentityExternalManage, false)]
public sealed record ListExternalLoginsQuery : IRequest<Result<IReadOnlyList<ExternalLinkView>>>;

[Authorize(Permissions.IdentityExternalManage, false)]
public sealed record UnlinkExternalLoginCommand(string Provider) : IRequest<Result>, ISensitiveRequest;
