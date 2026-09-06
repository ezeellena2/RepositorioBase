using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;

/// <summary>
/// "Prove it is still you", for one named action. It carries no `currentPassword` field anywhere else in the
/// system: this is the only place a password is re-typed, and what it produces is a server-side record rather than
/// anything the client holds (IA-REQ-051).
/// </summary>
[Authorize(Permissions.IdentityCredentialsManage, false)]
public sealed record ReauthenticateCommand(string Action, string Password) : IRequest<Result>, ISensitiveRequest;
