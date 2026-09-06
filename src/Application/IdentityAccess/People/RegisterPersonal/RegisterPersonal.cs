using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;

/// <summary>
/// A newcomer's own signup. It is public because the person has no account yet, and sensitive because it carries a
/// password and a document; neither is echoed and neither is logged.
/// </summary>
public sealed record RegisterPersonalCommand(
    string Email,
    string Password,
    string FullName,
    string DisplayName,
    string DocumentNumber) : IRequest<Result>, IPublicRequest, ISensitiveRequest;
