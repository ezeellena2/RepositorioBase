using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Infrastructure.Identity;

/// <summary>
/// Fails closed until the persisted-session adapter is introduced. It never
/// accepts client supplied tenant identifiers as an authority source.
/// </summary>
public sealed class CurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public TenantId? TenantId => accessor.HttpContext?.Items.TryGetValue(SessionCookieEvents.ValidatedSessionKey, out var value) == true
        ? (value as ValidatedSession)?.ActiveTenantId
        : null;
}
