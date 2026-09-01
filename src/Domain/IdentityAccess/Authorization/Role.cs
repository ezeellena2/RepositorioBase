using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public sealed class Role : BaseEntity<RoleId>
{
    private Role() { }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public bool IsSystem { get; private set; }

    public bool IsRetired { get; private set; }

    public static Role Create(Tenant tenant, string name) => Create(tenant, name, false);

    public static Role CreateSystem(Tenant tenant, string name) => Create(tenant, name, true);

    public void Rename(Tenant tenant, string name)
    {
        EnsureMutable(tenant);
        SetName(name);
        tenant.IncrementAuthorizationVersion();
    }

    public void Retire(Tenant tenant)
    {
        EnsureMutable(tenant);
        if (IsRetired)
        {
            throw new InvalidOperationException("Retired roles cannot transition.");
        }

        IsRetired = true;
        tenant.IncrementAuthorizationVersion();
    }

    private static Role Create(Tenant tenant, string name, bool isSystem)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(tenant));
        }

        var role = new Role
        {
            Id = RoleId.New(),
            TenantId = tenant.Id,
            IsSystem = isSystem
        };
        role.SetName(name);
        return role;
    }

    private void EnsureMutable(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id != TenantId)
        {
            throw new InvalidOperationException("Role does not belong to the supplied tenant.");
        }

        if (IsSystem)
        {
            throw new InvalidOperationException("System roles cannot be changed or retired.");
        }

        if (IsRetired)
        {
            throw new InvalidOperationException("Retired roles cannot be changed.");
        }
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Role names cannot be empty.", nameof(name));
        }

        var normalized = name.Trim();
        if (normalized.Length > 128)
        {
            throw new ArgumentException("Role names cannot exceed 128 characters.", nameof(name));
        }

        Name = normalized;
        NormalizedName = normalized.ToUpperInvariant();
    }
}
