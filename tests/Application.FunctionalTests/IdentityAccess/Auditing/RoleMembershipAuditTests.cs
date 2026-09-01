using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Auditing;

public sealed class RoleMembershipAuditTests : TestBase
{
    [Test]
    public async Task Role_and_membership_changes_persist_exact_allowlisted_audit_events_with_correlation()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"audit-authority-{Guid.NewGuid():N}"));
        var actorId = await TestApp.RunAsUserAsync($"audit-{Guid.NewGuid():N}@test.invalid", "Testing1234!", []);
        var membership = TenantMembership.CreateResponsible(tenant, actorId);
        var role = Role.Create(tenant, "Auditors");

        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(membership);
        await TestApp.AddAsync(role);

        var events = await TestApp.ListAsync<AuditEvent>();
        var persistedMembershipEvent = events.Single(auditEvent => auditEvent.EventType == "membership.changed");
        var persistedRoleEvent = events.Single(auditEvent => auditEvent.EventType == "role.changed");

        persistedMembershipEvent.EventType.ShouldBe("membership.changed");
        persistedRoleEvent.EventType.ShouldBe("role.changed");
        persistedMembershipEvent.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        persistedRoleEvent.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        persistedMembershipEvent.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "membership.changed", ["outcome"] = "changed" });
        persistedRoleEvent.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "role.changed", ["outcome"] = "changed" });
    }
}
