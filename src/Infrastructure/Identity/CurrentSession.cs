using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Infrastructure.Identity;

/// <summary>Exposes the session that cookie validation persisted for this request; it fails closed whenever none was published.</summary>
public sealed class CurrentSession(IHttpContextAccessor accessor) : ICurrentSession
{
    private ValidatedSession? Session => accessor.HttpContext?.Items.TryGetValue(SessionCookieEvents.ValidatedSessionKey, out var value) == true ? value as ValidatedSession : null;
    public UserSessionId? SessionId => Session?.SessionId;
    public Guid? IdentityId => Session?.IdentityId;
    public bool IsInvalid => Session is null;
}
