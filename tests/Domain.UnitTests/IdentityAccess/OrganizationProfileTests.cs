using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public class OrganizationProfileTests
{
    [Test]
    public void CuitIsNormalizedToElevenDigits()
    {
        var cuit = NormalizedCuit.From("30-71234567-4");

        cuit.Value.ShouldBe("30712345674");
        cuit.ToString().ShouldBe("30712345674");
    }

    [Test]
    public void CuitRejectsValuesThatCannotBeNormalizedToElevenDigits()
    {
        Should.Throw<ArgumentException>(() => NormalizedCuit.From("30-7123-token"));
        Should.Throw<ArgumentException>(() => NormalizedCuit.From("3071234567"));
    }

    [Test]
    public void ProfileBelongsOnlyToAnOrganizationTenant()
    {
        var organization = Tenant.CreateOrganization(TenantSlug.From("acme-sa"));
        var profile = OrganizationProfile.Create(organization, "Acme S.A.", NormalizedCuit.From("30-71234567-4"));

        profile.TenantId.ShouldBe(organization.Id);
        profile.LegalName.ShouldBe("Acme S.A.");
        profile.Cuit.Value.ShouldBe("30712345674");

        var personalTenant = Tenant.CreatePersonal(TenantSlug.From("jane-doe"));
        Should.Throw<InvalidOperationException>(() => OrganizationProfile.Create(personalTenant, "Jane Doe", NormalizedCuit.From("30-71234567-4")));
    }
}
