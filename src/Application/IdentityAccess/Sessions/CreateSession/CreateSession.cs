using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;

public sealed record CreateSessionCommand(string Email, string Password) : IRequest<Result<CreatedSession>>, IPublicRequest, ISensitiveRequest;

public sealed record CreatedSession(Guid IdentityId, Guid SessionId);
