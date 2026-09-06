using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;

/// <summary>
/// Somebody who already has an account adding their own `Personal` context. It carries no email and no password:
/// the session is the proof, and asking for a credential again would be asking a person to prove what they just
/// proved (IA-REQ-048).
/// </summary>
[Authorize(Permissions.IdentityProfileManage, false)]
public sealed record CreatePersonalContextCommand(
    string FullName,
    string DisplayName,
    string DocumentNumber) : IRequest<Result>, ISensitiveRequest;
