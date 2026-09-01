using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

/// <summary>
/// The validated server-side tenant context. Implementations must never derive
/// this value from request headers, routes, query strings, claims, or browser storage.
/// </summary>
public interface ICurrentTenant
{
    TenantId? TenantId { get; }
}
