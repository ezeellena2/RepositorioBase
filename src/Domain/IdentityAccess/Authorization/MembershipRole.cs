using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public sealed class MembershipRole
{
    private MembershipRole() { }

    public TenantId TenantId { get; private set; }

    public MembershipId MembershipId { get; private set; }

    public RoleId RoleId { get; private set; }

    public static MembershipRole Create(Tenant tenant, TenantMembership membership, Role role)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(role);
        if (membership.TenantId != tenant.Id || role.TenantId != tenant.Id)
        {
            throw new InvalidOperationException("Memberships and roles can only be associated within the same tenant.");
        }

        tenant.IncrementAuthorizationVersion();
        return new MembershipRole { TenantId = tenant.Id, MembershipId = membership.Id, RoleId = role.Id };
    }

    public void Revoke(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id != TenantId)
        {
            throw new InvalidOperationException("Membership roles can only be changed within their tenant.");
        }

        tenant.IncrementAuthorizationVersion();
    }
}
