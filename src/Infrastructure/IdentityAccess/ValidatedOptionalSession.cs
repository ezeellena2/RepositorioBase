using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>Exposes only the validated persisted session to public flows that accept an optional signed-in caller.</summary>
public sealed class ValidatedOptionalSession(IHttpContextAccessor accessor) : IValidatedOptionalSession
{

    private ValidatedSession? Session => accessor.HttpContext?.Items[SessionCookieEvents.ValidatedSessionKey] as ValidatedSession;
    private bool HasAuthenticationCookie => accessor.HttpContext?.Request.Cookies.ContainsKey(SessionCookieEvents.CookieName) == true;

    public Guid? IdentityId => Session?.IdentityId;
    public string? Email => Session?.Email;
    public bool IsInvalid => HasAuthenticationCookie && Session is null;
}
