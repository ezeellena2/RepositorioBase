using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.ChangePassword;

/// <summary>
/// Changing a password from inside the account. It carries no `currentPassword`: what authorizes it is the recent
/// identity proof, which a provider-only identity can also obtain and a stolen cookie cannot (IA-REQ-051).
/// </summary>
[Authorize(Permissions.IdentityCredentialsManage, false)]
public sealed record ChangePasswordCommand(string NewPassword) : IRequest<Result<ReplacedSession>>, ISensitiveRequest;

/// <summary>The session that replaced the one which asked. Web signs the caller into it in the same response.</summary>
public sealed record ReplacedSession(Guid IdentityId, Guid SessionId);
