using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions;

/// <summary>
/// The identity's own devices. It carries no subject and no paging: this is one person's short list, not a
/// directory, and there is no route that resolves anybody else's (IA-REQ-049).
/// </summary>
[Authorize(Permissions.IdentitySessionManage, false)]
public sealed record ListOwnSessionsQuery : IRequest<Result<IReadOnlyList<OwnSessionResponse>>>;

/// <summary>
/// What a device is shown as. Timestamps are truncated to the minute, the label comes from the closed server-side
/// set, and nothing here identifies a network or a browser build.
/// </summary>
public sealed record OwnSessionResponse(
    string SessionRef,
    bool IsCurrent,
    string DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt);

[Authorize(Permissions.IdentitySessionManage, false)]
public sealed record RevokeOwnSessionCommand(string SessionRef) : IRequest<Result>, ISensitiveRequest;

[Authorize(Permissions.IdentitySessionManage, false)]
public sealed record RevokeOtherSessionsCommand : IRequest<Result>, ISensitiveRequest;
