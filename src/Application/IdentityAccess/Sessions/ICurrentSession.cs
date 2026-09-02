using CleanArchitecture.Domain.IdentityAccess.Sessions;

namespace CleanArchitecture.Application.IdentityAccess.Sessions;

/// <summary>Validated persisted session context. Implementations must fail closed when it is absent or invalid.</summary>
public interface ICurrentSession
{
    UserSessionId? SessionId { get; }
    Guid? IdentityId { get; }
    bool IsInvalid { get; }
}
