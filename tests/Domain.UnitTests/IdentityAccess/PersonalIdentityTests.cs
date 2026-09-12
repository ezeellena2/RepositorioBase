using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

/// <summary>
/// The rules a person's own context keeps without a database: what a document number normalizes to, who may own a
/// Personal tenant, and what a profile refuses to say about itself (IA-REQ-050, IA-REQ-056).
/// </summary>
public sealed class PersonalIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Tenant Personal() => Tenant.CreatePersonal(TenantSlug.From($"personal-{Guid.NewGuid():N}"));

    private static IReadOnlyList<DocumentFingerprintValue> OneFingerprint() =>
        [new DocumentFingerprintValue(1, "k1:v1:" + new string('A', 43) + "=")];

    [Test]
    public void A_document_number_normalizes_away_the_separators_a_person_types()
    {
        var typed = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, " 12.345.678 ");
        var plain = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "12345678");

        typed.Number.ShouldBe("12345678");
        typed.ShouldBe(plain, "the same person typing the same document two ways is one documentary identity");
    }

    [Test]
    public void A_leading_zero_does_not_make_a_second_person()
    {
        NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "07123456")
            .ShouldBe(NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "7123456"));
    }

    [Test]
    public void A_document_number_that_is_not_an_argentine_dni_is_refused()
    {
        Should.Throw<ArgumentException>(() => NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "  "));
        Should.Throw<ArgumentException>(() => NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "12345"));
        Should.Throw<ArgumentException>(() => NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "123456789"));
        Should.Throw<ArgumentException>(() => NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "1234567X"));
    }

    [TestCase("1234567X", DocumentRule.Characters)]
    [TestCase("12345", DocumentRule.Length)]
    [TestCase("123456789", DocumentRule.Length)]
    public void A_document_rule_identifies_what_the_person_can_correct(string value, DocumentRule expected)
    {
        NormalizedDocument.Evaluate(value, out var canonicalNumber).ShouldBe(expected);
        canonicalNumber.ShouldBeEmpty("an invalid input must not escape through the evaluator's output");
    }

    [Test]
    public void Document_evaluation_preserves_separator_and_leading_zero_normalization()
    {
        NormalizedDocument.Evaluate(" 07.123.456 ", out var canonicalNumber)
            .ShouldBe(DocumentRule.Satisfied);
        canonicalNumber.ShouldBe("7123456");
    }

    [Test]
    public void A_document_never_prints_its_own_digits()
    {
        var document = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "12345678");

        document.ToString().Contains("12345678", StringComparison.Ordinal)
            .ShouldBeFalse("a value object reaches logs by accident; its digits must not");
        document.ToString().ShouldEndWith("78");
        document.Canonical.ShouldBe("AR|DNI|12345678", "the canonical tuple is what the protector and the fingerprint see");
    }

    [Test]
    public void A_person_profile_belongs_only_to_a_personal_tenant()
    {
        var personal = Personal();
        var identityId = Guid.NewGuid();

        var profile = PersonProfile.Create(personal, identityId, " Jane Doe ", " Jane ", DataClassification.Synthetic);

        profile.IdentityId.ShouldBe(identityId);
        profile.PersonalTenantId.ShouldBe(personal.Id);
        profile.FullName.ShouldBe("Jane Doe");
        profile.DisplayName.ShouldBe("Jane");
        profile.Classification.ShouldBe(DataClassification.Synthetic);

        var organization = Tenant.CreateOrganization(TenantSlug.From("acme-sa"));
        Should.Throw<InvalidOperationException>(() => PersonProfile.Create(organization, identityId, "Jane Doe", "Jane", DataClassification.Synthetic));
    }

    [Test]
    public void A_person_profile_refuses_an_empty_identity_or_an_empty_name()
    {
        var personal = Personal();

        Should.Throw<ArgumentException>(() => PersonProfile.Create(personal, Guid.Empty, "Jane Doe", "Jane", DataClassification.Synthetic));
        Should.Throw<ArgumentException>(() => PersonProfile.Create(personal, Guid.NewGuid(), "   ", "Jane", DataClassification.Synthetic));
        Should.Throw<ArgumentException>(() => PersonProfile.Create(personal, Guid.NewGuid(), "Jane Doe", "   ", DataClassification.Synthetic));
    }

    [Test]
    public void Renaming_reports_whether_anything_actually_changed()
    {
        var profile = PersonProfile.Create(Personal(), Guid.NewGuid(), "Jane Doe", "Jane", DataClassification.Synthetic);

        profile.Rename("Jane Doe", "Jane").ShouldBeFalse("an edit that changes nothing is not an edit");
        profile.Rename(" Jane Q. Doe ", "Jane").ShouldBeTrue();
        profile.FullName.ShouldBe("Jane Q. Doe");
        profile.DisplayName.ShouldBe("Jane");
        Should.Throw<ArgumentException>(() => profile.Rename("Jane Q. Doe", "  "));
    }

    [Test]
    public void Ownership_of_a_personal_tenant_is_an_explicit_row_rather_than_a_convention()
    {
        var personal = Personal();
        var identityId = Guid.NewGuid();

        var ownership = PersonalTenantOwnership.Create(personal, identityId);

        ownership.IdentityId.ShouldBe(identityId);
        ownership.TenantId.ShouldBe(personal.Id);

        Should.Throw<InvalidOperationException>(() => PersonalTenantOwnership.Create(Tenant.CreateOrganization(TenantSlug.From("acme-sa")), identityId));
        Should.Throw<ArgumentException>(() => PersonalTenantOwnership.Create(personal, Guid.Empty));
    }

    [Test]
    public void A_recorded_document_holds_ciphertext_and_fingerprints_and_no_number()
    {
        var identityId = Guid.NewGuid();

        var document = IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            "protected-payload",
            OneFingerprint(),
            DataClassification.Synthetic,
            Now);

        document.IdentityId.ShouldBe(identityId);
        document.Country.ShouldBe(IdentityDocumentCountry.AR);
        document.DocumentType.ShouldBe(IdentityDocumentKind.DNI);
        document.Ciphertext.ShouldBe("protected-payload");
        document.RecordedAt.ShouldBe(Now);
        document.PurgedAt.ShouldBeNull();
        document.Fingerprints.Count.ShouldBe(1);
        document.Fingerprints.Single().KeyVersion.ShouldBe(1);
    }

    [Test]
    public void A_recorded_document_refuses_material_it_cannot_trust()
    {
        var identityId = Guid.NewGuid();

        Should.Throw<ArgumentException>(() => IdentityDocument.Record(
            identityId, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "   ", OneFingerprint(), DataClassification.Synthetic, Now));

        Should.Throw<ArgumentException>(() => IdentityDocument.Record(
            identityId, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "protected-payload", [], DataClassification.Synthetic, Now));

        Should.Throw<ArgumentException>(() => IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            "protected-payload",
            [new DocumentFingerprintValue(1, "not-a-versioned-fingerprint")],
            DataClassification.Synthetic,
            Now));

        Should.Throw<ArgumentException>(() => IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            "protected-payload",
            [new DocumentFingerprintValue(0, "k0:v1:" + new string('A', 43) + "=")],
            DataClassification.Synthetic,
            Now));
    }

    [Test]
    public void A_purge_removes_the_ciphertext_and_the_fingerprints_that_made_it_findable()
    {
        var document = IdentityDocument.Record(
            Guid.NewGuid(), IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, "protected-payload", OneFingerprint(), DataClassification.Synthetic, Now);

        document.Purge(Now.AddDays(1));

        document.Ciphertext.ShouldBeEmpty("a purged number leaves no ciphertext behind");
        document.Fingerprints.ShouldBeEmpty("a purged number is reclaimable, so its fingerprint stops occupying the index");
        document.PurgedAt.ShouldBe(Now.AddDays(1));
        Should.Throw<InvalidOperationException>(() => document.Purge(Now.AddDays(2)));
    }
}
