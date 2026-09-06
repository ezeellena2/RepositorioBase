using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;

/// <summary>
/// "I forgot my password." It is public because somebody who cannot sign in is the only person who needs it, and
/// its answer is the same for every address: telling a caller whether one has an account would make this the
/// account-enumeration route the rest of the system is careful not to be (IA-REQ-029).
/// </summary>
public sealed record RequestPasswordRecoveryCommand(string Email) : IRequest<Result>, IPublicRequest, ISensitiveRequest;

/// <summary>
/// Spending the mailed link. Public for the same reason, and sensitive because it carries both a usable token and
/// a new password.
/// </summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>, IPublicRequest, ISensitiveRequest;
