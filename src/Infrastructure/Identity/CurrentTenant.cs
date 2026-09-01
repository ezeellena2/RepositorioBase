using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Infrastructure.Identity;

/// <summary>
/// Fails closed until the persisted-session adapter is introduced. It never
/// accepts client supplied tenant identifiers as an authority source.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public TenantId? TenantId => null;
}
