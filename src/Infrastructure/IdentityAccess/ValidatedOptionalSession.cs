using CleanArchitecture.Application.IdentityAccess.Sessions;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>Task 7 deliberately fails closed for supplied identity cookies until Task 8 validates them.</summary>
public sealed class ValidatedOptionalSession(IHttpContextAccessor accessor) : IValidatedOptionalSession
{
    private const string IdentityCookie = ".AspNetCore.Identity.Application";
    private bool HasCookie => accessor.HttpContext?.Request.Cookies.ContainsKey(IdentityCookie) == true;
    public Guid? IdentityId => null;
    public string? Email => null;
    public bool IsInvalid => HasCookie;
}
