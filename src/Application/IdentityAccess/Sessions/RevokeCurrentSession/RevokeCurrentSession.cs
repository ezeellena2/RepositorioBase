using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.RevokeCurrentSession;

[Authorize(Permissions.IdentitySessionManage, false)]
public sealed record RevokeCurrentSessionCommand : IRequest<Result>;
