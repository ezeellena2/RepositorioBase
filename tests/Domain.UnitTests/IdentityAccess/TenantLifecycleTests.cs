using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public class TenantLifecycleTests
{
    [Test]
    public void ResponsibleMembershipRemainsPendingUntilTheOrganizationTenantActivates()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("acme-sa"));
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());

        tenant.Status.ShouldBe(TenantStatus.PendingConfirmation);
        membership.Status.ShouldBe(MembershipStatus.PendingConfirmation);
        Should.Throw<InvalidOperationException>(() => membership.Activate(tenant));

        tenant.Activate();
        membership.Activate(tenant);

        membership.Status.ShouldBe(MembershipStatus.Active);
        tenant.AuthorizationVersion.ShouldBe(2);
    }

    [Test]
    public void SuspendedAndClosedTenantsRejectFurtherActivation()
    {
        var suspendedTenant = Tenant.CreateOrganization(TenantSlug.From("suspended-org"));
        suspendedTenant.Activate();
        suspendedTenant.Suspend(TenantSuspensionReason.PolicyViolation, DateTimeOffset.UtcNow);

        suspendedTenant.Status.ShouldBe(TenantStatus.Suspended);
        Should.Throw<InvalidOperationException>(() => suspendedTenant.Activate());

        var closedTenant = Tenant.CreateOrganization(TenantSlug.From("closed-org"));
        closedTenant.Close();

        closedTenant.Status.ShouldBe(TenantStatus.Closed);
        Should.Throw<InvalidOperationException>(() => closedTenant.Activate());
    }

    [Test]
    public void MembershipStateChangesIncrementTheTenantAuthorizationVersion()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From("versioned-org"));
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        tenant.Activate();
        membership.Activate(tenant);

        membership.Suspend(tenant);

        membership.Status.ShouldBe(MembershipStatus.Suspended);
        tenant.AuthorizationVersion.ShouldBe(3);
        Should.Throw<InvalidOperationException>(() => membership.Suspend(tenant));
    }

    [Test]
    public void StrongIdsDoNotExposePublicGuidConstructors()
    {
        typeof(TenantId).GetConstructor([typeof(Guid)]).ShouldBeNull();
        typeof(MembershipId).GetConstructor([typeof(Guid)]).ShouldBeNull();
    }

    [Test]
    public void TenantFactoryRejectsTheDefaultSlug()
    {
        Should.Throw<ArgumentException>(() => Tenant.CreateOrganization(default));
    }
}
