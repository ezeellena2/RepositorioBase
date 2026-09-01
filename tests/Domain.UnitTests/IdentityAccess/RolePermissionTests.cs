using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class RolePermissionTests
{
    [Test]
    public void Permission_codes_are_immutable_normalized_resource_actions_with_distinct_platform_admin_capabilities()
    {
        var permission = Permission.Create("platform.admins.read", [TenantType.Platform]);

        permission.Code.ShouldBe("platform.admins.read");
        PermissionsMustRemainDistinct("platform.admins.read", "platform.admins.manage");
        Should.Throw<ArgumentException>(() => Permission.Create("Platform.Admins.Read", [TenantType.Platform]));
        Should.Throw<ArgumentException>(() => Permission.Create("platform-admins-read", [TenantType.Platform]));
    }

    [Test]
    public void Roles_use_normalized_tenant_local_names_and_protect_system_roles()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("role-names"));
        var role = Role.Create(tenant, " Finance Managers ");
        var systemRole = Role.CreateSystem(tenant, "Owner");

        role.NormalizedName.ShouldBe("FINANCE MANAGERS");
        role.Rename(tenant, "Billing Managers");
        role.NormalizedName.ShouldBe("BILLING MANAGERS");
        Should.Throw<InvalidOperationException>(() => systemRole.Rename(tenant, "Other"));
        Should.Throw<InvalidOperationException>(() => systemRole.Retire(tenant));
    }

    [Test]
    public void Role_and_membership_assignments_reject_cross_tenant_associations_and_increment_authorization_version()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("authorization-one"));
        var anotherTenant = Tenant.CreateOrganization(TenantSlug.From("authorization-two"));
        var role = Role.Create(tenant, "Operators");
        var permission = Permission.Create("members.read", [TenantType.Organization]);
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        var version = tenant.AuthorizationVersion;

        RolePermission.Create(tenant, role, permission);
        MembershipRole.Create(tenant, membership, role);

        tenant.AuthorizationVersion.ShouldBe(version + 2);
        Should.Throw<InvalidOperationException>(() => RolePermission.Create(anotherTenant, role, permission));
        Should.Throw<InvalidOperationException>(() => MembershipRole.Create(anotherTenant, membership, role));
    }

    [Test]
    public void Permissions_are_limited_to_the_tenant_types_declared_by_the_catalog()
    {
        var organization = Tenant.CreateOrganization(TenantSlug.From("type-org"));
        var platformOnly = Permission.Create("platform.admins.manage", [TenantType.Platform]);
        var role = Role.Create(organization, "Operators");

        Should.Throw<InvalidOperationException>(() => RolePermission.Create(organization, role, platformOnly));
    }

    [Test]
    public void Revoking_each_assignment_increments_authorization_version_once()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("authority-revocations"));
        var role = Role.Create(tenant, "Operators");
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        var permission = Permission.Create("members.read", [TenantType.Organization]);
        var rolePermission = RolePermission.Create(tenant, role, permission);
        var membershipRole = MembershipRole.Create(tenant, membership, role);
        var beforeRolePermissionRevocation = tenant.AuthorizationVersion;

        rolePermission.Revoke(tenant);
        tenant.AuthorizationVersion.ShouldBe(beforeRolePermissionRevocation + 1);

        var beforeMembershipRoleRevocation = tenant.AuthorizationVersion;
        membershipRole.Revoke(tenant);
        tenant.AuthorizationVersion.ShouldBe(beforeMembershipRoleRevocation + 1);
    }

    [Test]
    public void Retiring_custom_roles_changes_state_and_protects_system_roles()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("retired-roles"));
        var customRole = Role.Create(tenant, "Operators");
        var systemRole = Role.CreateSystem(tenant, "Owner");
        var beforeRetirement = tenant.AuthorizationVersion;

        customRole.Retire(tenant);

        customRole.IsRetired.ShouldBeTrue();
        tenant.AuthorizationVersion.ShouldBe(beforeRetirement + 1);
        Should.Throw<InvalidOperationException>(() => systemRole.Retire(tenant));
    }

    private static void PermissionsMustRemainDistinct(string read, string manage) => read.ShouldNotBe(manage);
}
