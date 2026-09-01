using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public sealed class RolePermission
{
    private RolePermission() { }

    public TenantId TenantId { get; private set; }

    public RoleId RoleId { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;

    public static RolePermission Create(Tenant tenant, Role role, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);
        if (tenant.Id != role.TenantId)
        {
            throw new InvalidOperationException("Roles can only receive permissions within their tenant.");
        }

        if (!permission.AllowedTenantTypes.Contains(tenant.Type))
        {
            throw new InvalidOperationException("The permission is not allowed for this tenant type.");
        }

        tenant.IncrementAuthorizationVersion();
        return new RolePermission { TenantId = tenant.Id, RoleId = role.Id, PermissionCode = permission.Code };
    }

    public void Revoke(Tenant tenant)
    {
        EnsureTenant(tenant);
        tenant.IncrementAuthorizationVersion();
    }

    private void EnsureTenant(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id != TenantId)
        {
            throw new InvalidOperationException("Role permissions can only be changed within their tenant.");
        }
    }
}
