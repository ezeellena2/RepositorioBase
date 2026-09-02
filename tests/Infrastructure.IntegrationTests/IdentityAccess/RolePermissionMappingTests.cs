using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Claims;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class RolePermissionMappingTests
{
    [Test]
    public async Task Catalog_synchronization_is_idempotent_and_keeps_platform_admin_read_and_manage_distinct()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<PermissionCatalogSynchronizer>();

        await synchronizer.SynchronizeAsync(CancellationToken.None);
        await synchronizer.SynchronizeAsync(CancellationToken.None);

        var catalogCodes = Permissions.Catalog.Select(definition => definition.Code).Order(StringComparer.Ordinal).ToArray();
        var persistedCodes = await context.Permissions.Where(permission => catalogCodes.Contains(permission.Code)).Select(permission => permission.Code).Order().ToArrayAsync();

        persistedCodes.ShouldBe(catalogCodes);
        persistedCodes.ShouldContain(Permissions.PlatformAdminsRead);
        persistedCodes.ShouldContain(Permissions.PlatformAdminsManage);
        Permissions.PlatformAdminsRead.ShouldNotBe(Permissions.PlatformAdminsManage);
    }

    [Test]
    public void Model_maps_tenant_scoped_role_associations_with_composite_foreign_keys()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;
        var role = model.FindEntityType(typeof(Role));
        var permission = model.FindEntityType(typeof(Permission));
        var rolePermission = model.FindEntityType(typeof(RolePermission));
        var membershipRole = model.FindEntityType(typeof(MembershipRole));

        role.ShouldNotBeNull();
        permission.ShouldNotBeNull();
        rolePermission.ShouldNotBeNull();
        membershipRole.ShouldNotBeNull();
        role!.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        role.FindProperty(nameof(Role.IsRetired))!.IsNullable.ShouldBeFalse();
        role.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "NormalizedName"])).IsUnique.ShouldBeTrue();
        permission!.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("Code");
        permission.FindProperty(nameof(Permission.AllowedTenantTypes))!.GetValueConverter().ShouldNotBeNull();
        rolePermission!.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["TenantId", "RoleId", "PermissionCode"]);
        rolePermission.GetForeignKeys().Single(key => key.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "RoleId"])).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        membershipRole!.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["TenantId", "MembershipId", "RoleId"]);
        membershipRole.GetForeignKeys().Single(key => key.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "MembershipId"])).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        membershipRole.GetForeignKeys().Single(key => key.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "RoleId"])).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task Database_rejects_cross_tenant_role_and_membership_associations()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = Tenant.CreateOrganization(TenantSlug.From($"role-map-a-{Guid.NewGuid():N}"));
        var second = Tenant.CreateOrganization(TenantSlug.From($"role-map-b-{Guid.NewGuid():N}"));
        var firstRole = Role.Create(first, "Operators");
        var secondRole = Role.Create(second, "Operators");
        var identityId = Guid.NewGuid();
        var membership = TenantMembership.CreateResponsible(first, identityId);
        var user = new ApplicationUser { Id = identityId, UserName = $"role-map-{identityId:N}", Email = $"role-map-{identityId:N}@test.invalid" };
        context.AddRange(user, first, second, firstRole, secondRole, membership);
        await context.SaveChangesAsync();

        var membershipRoleFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"INSERT INTO \"MembershipRoles\" (\"TenantId\", \"MembershipId\", \"RoleId\") VALUES ({first.Id.Value}, {membership.Id.Value}, {secondRole.Id.Value})"));
        membershipRoleFailure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);

        var rolePermissionFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"INSERT INTO \"RolePermissions\" (\"TenantId\", \"RoleId\", \"PermissionCode\") VALUES ({first.Id.Value}, {secondRole.Id.Value}, {Permissions.MembersRead})"));
        rolePermissionFailure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task Evaluator_uses_only_the_explicit_active_membership_for_the_requested_tenant()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identityId = Guid.NewGuid();
        var user = new ApplicationUser { Id = identityId, UserName = $"permission-{identityId:N}", Email = $"permission-{identityId:N}@test.invalid" };
        var grantedTenant = Tenant.CreateOrganization(TenantSlug.From($"permission-granted-{Guid.NewGuid():N}"));
        var otherTenant = Tenant.CreateOrganization(TenantSlug.From($"permission-denied-{Guid.NewGuid():N}"));
        var grantedMembership = TenantMembership.CreateResponsible(grantedTenant, identityId);
        var otherMembership = TenantMembership.CreateResponsible(otherTenant, identityId);
        grantedTenant.Activate();
        otherTenant.Activate();
        grantedMembership.Activate(grantedTenant);
        otherMembership.Activate(otherTenant);
        var role = Role.Create(grantedTenant, "Operators");
        var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == Permissions.MembersRead);
        var rolePermission = RolePermission.Create(grantedTenant, role, permission);
        var membershipRole = MembershipRole.Create(grantedTenant, grantedMembership, role);
        context.AddRange(user, grantedTenant, otherTenant, grantedMembership, otherMembership, role, rolePermission, membershipRole);
        await context.SaveChangesAsync();

        (await evaluator.HasPermissionAsync(identityId, grantedTenant.Id, Permissions.MembersRead)).ShouldBeTrue();
        (await evaluator.HasPermissionAsync(identityId, otherTenant.Id, Permissions.MembersRead)).ShouldBeFalse();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, identityId.ToString())],
                "integration-test"))
        };
        (await evaluator.HasPermissionAsync(identityId, Permissions.TodosRead)).ShouldBeFalse("known application permissions require an explicit persisted grant");

        // The protected session ticket only references the identity and the session. A principal claim is
        // never an authority source for application permissions; only the persisted user claim grants them.
        httpContextAccessor.HttpContext.User.AddIdentity(new ClaimsIdentity(
            [new Claim(Permissions.ApplicationPermissionClaimType, Permissions.TodosRead)],
            "integration-test"));
        (await evaluator.HasPermissionAsync(identityId, Permissions.TodosRead)).ShouldBeFalse("principal claims must not grant application permissions");

        context.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = identityId, ClaimType = Permissions.ApplicationPermissionClaimType, ClaimValue = Permissions.TodosRead });
        await context.SaveChangesAsync();
        (await evaluator.HasPermissionAsync(identityId, Permissions.TodosRead)).ShouldBeTrue();
        (await evaluator.HasPermissionAsync(identityId, Permissions.IdentityContextRead)).ShouldBeTrue("session self-service capabilities need only the matching validated identity");
        (await evaluator.HasPermissionAsync(Guid.NewGuid(), Permissions.IdentityContextRead)).ShouldBeFalse("a mismatched identity never receives self-service capabilities");
        (await evaluator.HasPermissionAsync(identityId, "future.unregistered.permission")).ShouldBeFalse("unregistered application permission codes must fail closed");
    }

    [Test]
    public async Task Direct_assignment_removals_increment_authorization_version_and_write_revocation_audits()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityId = Guid.NewGuid();
        var user = new ApplicationUser { Id = identityId, UserName = $"revoke-{identityId:N}", Email = $"revoke-{identityId:N}@test.invalid" };
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"revoke-authority-{Guid.NewGuid():N}"));
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        tenant.Activate();
        membership.Activate(tenant);
        var role = Role.Create(tenant, "Operators");
        var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == Permissions.MembersRead);
        context.AddRange(user, tenant, membership, role);
        await context.SaveChangesAsync();
        var beforeRolePermissionGrant = tenant.AuthorizationVersion;
        var rolePermission = RolePermission.Create(tenant, role, permission);

        context.Add(rolePermission);
        await context.SaveChangesAsync();

        tenant.AuthorizationVersion.ShouldBe(beforeRolePermissionGrant + 1);
        var roleGrantAudit = (await context.AuditEvents.Where(audit => audit.TenantId == tenant.Id && audit.EventType == "role.changed").ToListAsync())
            .Single(audit => audit.Metadata["outcome"] == "granted");
        roleGrantAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "role.changed", ["outcome"] = "granted" });
        roleGrantAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        var beforeMembershipRoleGrant = tenant.AuthorizationVersion;
        var membershipRole = MembershipRole.Create(tenant, membership, role);

        context.Add(membershipRole);
        await context.SaveChangesAsync();

        tenant.AuthorizationVersion.ShouldBe(beforeMembershipRoleGrant + 1);
        var membershipGrantAudit = (await context.AuditEvents.Where(audit => audit.TenantId == tenant.Id && audit.EventType == "membership.changed").ToListAsync())
            .Single(audit => audit.Metadata["outcome"] == "granted");
        membershipGrantAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "membership.changed", ["outcome"] = "granted" });
        membershipGrantAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        var beforeRolePermissionRemoval = tenant.AuthorizationVersion;

        context.Remove(rolePermission);
        await context.SaveChangesAsync();

        tenant.AuthorizationVersion.ShouldBe(beforeRolePermissionRemoval + 1);
        var roleRevocationAudit = (await context.AuditEvents.Where(audit => audit.TenantId == tenant.Id && audit.EventType == "role.changed").ToListAsync())
            .Single(audit => audit.Metadata["outcome"] == "revoked");
        roleRevocationAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "role.changed", ["outcome"] = "revoked" });
        roleRevocationAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        var beforeMembershipRoleRemoval = tenant.AuthorizationVersion;

        context.Remove(membershipRole);
        await context.SaveChangesAsync();

        tenant.AuthorizationVersion.ShouldBe(beforeMembershipRoleRemoval + 1);
        var membershipRevocationAudit = (await context.AuditEvents.Where(audit => audit.TenantId == tenant.Id && audit.EventType == "membership.changed").ToListAsync())
            .Single(audit => audit.Metadata["outcome"] == "revoked");
        membershipRevocationAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "membership.changed", ["outcome"] = "revoked" });
        membershipRevocationAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Custom_role_rename_retire_and_deletion_each_increment_authorization_once_and_audit_the_transition()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"role-transition-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        context.AddRange(tenant, role);
        await context.SaveChangesAsync();

        var beforeRename = tenant.AuthorizationVersion;
        role.Rename(tenant, "Support Operators");
        await context.SaveChangesAsync();
        tenant.AuthorizationVersion.ShouldBe(beforeRename + 1);

        var beforeRetire = tenant.AuthorizationVersion;
        role.Retire(tenant);
        await context.SaveChangesAsync();
        tenant.AuthorizationVersion.ShouldBe(beforeRetire + 1);

        var beforeDeletion = tenant.AuthorizationVersion;
        context.Remove(role);
        await context.SaveChangesAsync();
        tenant.AuthorizationVersion.ShouldBe(beforeDeletion + 1);

        var roleAudits = await context.AuditEvents.Where(audit => audit.TenantId == tenant.Id && audit.EventType == "role.changed").ToListAsync();
        roleAudits.Single(audit => audit.Metadata["outcome"] == "retired").Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "role.changed", ["outcome"] = "retired" });
        roleAudits.Single(audit => audit.Metadata["outcome"] == "deleted").Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "role.changed", ["outcome"] = "deleted" });
    }

    [Test]
    public async Task Direct_system_role_deletion_is_rejected_without_authorization_or_audit_side_effects()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"system-role-{Guid.NewGuid():N}"));
        var role = Role.CreateSystem(tenant, "Owner");
        context.AddRange(tenant, role);
        await context.SaveChangesAsync();
        var before = tenant.AuthorizationVersion;
        var auditCount = await context.AuditEvents.CountAsync(audit => audit.TenantId == tenant.Id);

        context.Remove(role);

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        (await context.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id)).AuthorizationVersion.ShouldBe(before);
        (await context.AuditEvents.CountAsync(audit => audit.TenantId == tenant.Id)).ShouldBe(auditCount);
        var rawDeletionFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"DELETE FROM \"Roles\" WHERE \"Id\" = {role.Id.Value}"));
        rawDeletionFailure.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
        (await context.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id)).AuthorizationVersion.ShouldBe(before);
        (await context.AuditEvents.CountAsync(audit => audit.TenantId == tenant.Id)).ShouldBe(auditCount);
    }

    [Test]
    public async Task Retired_roles_and_stale_catalog_permissions_fail_closed()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<PermissionCatalogSynchronizer>();
        var identityId = Guid.NewGuid();
        var user = new ApplicationUser { Id = identityId, UserName = $"stale-{identityId:N}", Email = $"stale-{identityId:N}@test.invalid" };
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"stale-catalog-{Guid.NewGuid():N}"));
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        tenant.Activate();
        membership.Activate(tenant);
        var role = Role.Create(tenant, "Operators");
        var activePermission = await context.Permissions.SingleAsync(candidate => candidate.Code == Permissions.MembersRead);
        var activeGrant = RolePermission.Create(tenant, role, activePermission);
        context.AddRange(user, tenant, membership, role, activeGrant, MembershipRole.Create(tenant, membership, role));
        await context.SaveChangesAsync();
        (await evaluator.HasPermissionAsync(identityId, tenant.Id, Permissions.MembersRead)).ShouldBeTrue();

        membership.Suspend(tenant);
        await context.SaveChangesAsync();
        (await evaluator.HasPermissionAsync(identityId, tenant.Id, Permissions.MembersRead)).ShouldBeFalse("a suspended membership must never retain a tenant permission");

        tenant.Suspend();
        await context.SaveChangesAsync();
        (await evaluator.HasPermissionAsync(identityId, tenant.Id, Permissions.MembersRead)).ShouldBeFalse("a suspended tenant must never retain a tenant permission");

        role.Retire(tenant);
        await context.SaveChangesAsync();
        (await evaluator.HasPermissionAsync(identityId, tenant.Id, Permissions.MembersRead)).ShouldBeFalse();

        var stalePermission = Permission.Create("legacy.reports.read", [TenantType.Organization]);
        var staleRole = Role.Create(tenant, "Legacy Operators");
        var staleGrant = RolePermission.Create(tenant, staleRole, stalePermission);
        context.AddRange(stalePermission, staleRole, staleGrant, MembershipRole.Create(tenant, membership, staleRole));
        await context.SaveChangesAsync();

        (await evaluator.HasPermissionAsync(identityId, tenant.Id, stalePermission.Code)).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => synchronizer.SynchronizeAsync(CancellationToken.None));

        context.RemoveRange(context.MembershipRoles.Where(assignment => assignment.TenantId == tenant.Id && assignment.RoleId == staleRole.Id));
        context.Remove(staleGrant);
        context.Remove(stalePermission);
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task Failed_authority_transaction_rolls_back_version_and_audit_effects()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"audit-rollback-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == Permissions.MembersRead);
        var grant = RolePermission.Create(tenant, role, permission);
        context.AddRange(tenant, role, grant);
        await context.SaveChangesAsync();
        var versionBeforeFailure = tenant.AuthorizationVersion;
        var auditCountBeforeFailure = await context.AuditEvents.CountAsync(audit => audit.TenantId == tenant.Id);

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Remove(grant);
            await context.SaveChangesAsync();

            var databaseFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("INSERT INTO \"Roles\" (\"Id\") VALUES ('not-a-uuid');"));
            databaseFailure.SqlState.ShouldBe(PostgresErrorCodes.InvalidTextRepresentation);
            await transaction.RollbackAsync();
        });
        context.ChangeTracker.Clear();

        (await context.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id)).AuthorizationVersion.ShouldBe(versionBeforeFailure);
        (await context.AuditEvents.CountAsync(audit => audit.TenantId == tenant.Id)).ShouldBe(auditCountBeforeFailure);
        (await context.RolePermissions.AnyAsync(assignment => assignment.TenantId == tenant.Id && assignment.RoleId == role.Id && assignment.PermissionCode == permission.Code)).ShouldBeTrue();
    }
}
