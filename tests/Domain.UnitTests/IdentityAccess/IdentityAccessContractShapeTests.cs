using System.Reflection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public class IdentityAccessContractShapeTests
{
    private static readonly Assembly DomainAssembly = typeof(Common.BaseEntity).Assembly;

    [TestCase("CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Organizations.OrganizationProfile")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Memberships.TenantMembership")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Auditing.AuditEvent")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.Role")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.RoleId")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.Permission")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.RolePermission")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.MembershipRole")]
    public void RequiredAggregateTypeExists(string fullyQualifiedName)
    {
        DomainAssembly.GetType(fullyQualifiedName).ShouldNotBeNull();
    }

    [Test]
    public void TenantStatusExposesPendingConfirmation()
    {
        var tenantStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Tenants.TenantStatus");

        tenantStatus.ShouldNotBeNull();
        Enum.GetNames(tenantStatus!).ShouldContain("PendingConfirmation");
    }

    [Test]
    public void MembershipStatusExposesPendingConfirmation()
    {
        var membershipStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Memberships.MembershipStatus");

        membershipStatus.ShouldNotBeNull();
        Enum.GetNames(membershipStatus!).ShouldContain("PendingConfirmation");
    }

}
