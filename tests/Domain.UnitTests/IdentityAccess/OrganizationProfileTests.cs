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
        var cuit = NormalizedCuit.From("30-12345678-1");

        cuit.Value.ShouldBe("30123456781");
        cuit.ToString().ShouldBe("30123456781");
    }

    [TestCase("30-12345678-1", CuitRule.Satisfied)]
    [TestCase("20-12345678-6", CuitRule.Satisfied)]
    [TestCase("99-12345678-1", CuitRule.Satisfied)]
    [TestCase("30-12345678-9", CuitRule.CheckDigit)]
    [TestCase("20-00000001-0", CuitRule.CheckDigit)]
    [TestCase("30-1234567-1", CuitRule.Length)]
    [TestCase("30-ABCDEFGH-1", CuitRule.Characters)]
    public void SubmittedCuitReportsTheExactBrokenRule(string value, CuitRule expected)
    {
        NormalizedCuit.Evaluate(value, out _).ShouldBe(expected);
    }

    [Test]
    public void InputRejectsAWrongCheckDigitWithoutEchoingTheValue()
    {
        const string submitted = "30-12345678-9";

        var exception = Should.Throw<ArgumentException>(() => NormalizedCuit.From(submitted));

        exception.Message.ShouldContain(nameof(CuitRule.CheckDigit));
        exception.Message.ShouldNotContain(submitted);
        exception.Message.ShouldNotContain("12345678");
    }

    [Test]
    public void InputAcceptsAValidCheckDigitWithoutRestrictingThePrefix()
    {
        NormalizedCuit.From("99-12345678-1").Value.ShouldBe("99123456781");
    }

    [Test]
    public void InputMapsAModuloResultOfElevenToCheckDigitZero()
    {
        NormalizedCuit.From("30-87654321-0").Value.ShouldBe("30876543210");
    }

    [Test]
    public void StoredCuitAcceptsLegacyDigitsWithoutReapplyingTheInputRule()
    {
        var cuit = NormalizedCuit.FromStored("30123456789");

        cuit.Value.ShouldBe("30123456789");
    }

    [TestCase("30-12345678-1")]
    [TestCase("3012345678")]
    [TestCase("3012345678A")]
    [TestCase("３０１２３４５６７８１")]
    public void StoredCuitRejectsAnythingButElevenAsciiDigits(string stored)
    {
        var exception = Should.Throw<InvalidOperationException>(() => NormalizedCuit.FromStored(stored));

        exception.Message.ShouldBe("A stored CUIT is not eleven digits.");
        exception.Message.ShouldNotContain(stored);
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
        var profile = OrganizationProfile.Create(organization, "Acme S.A.", NormalizedCuit.From("30-12345678-1"));

        profile.TenantId.ShouldBe(organization.Id);
        profile.LegalName.ShouldBe("Acme S.A.");
        profile.Cuit.Value.ShouldBe("30123456781");

        var personalTenant = Tenant.CreatePersonal(TenantSlug.From("jane-doe"));
        Should.Throw<InvalidOperationException>(() => OrganizationProfile.Create(personalTenant, "Jane Doe", NormalizedCuit.From("30-12345678-1")));
    }

    [Test]
    public void ProfileFactoryRejectsTheDefaultNormalizedCuit()
    {
        var organization = Tenant.CreateOrganization(TenantSlug.From("acme-sa"));

        Should.Throw<ArgumentException>(() => OrganizationProfile.Create(organization, "Acme S.A.", default));
    }
}
