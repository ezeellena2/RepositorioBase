using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What PostgreSQL itself holds true about a person's own context (IA-REQ-050, IA-REQ-056).
/// <para>
/// The entity types are named as strings rather than imported, so this file states the shape the model must reach
/// before any of it exists: an absent type fails the assertion instead of the compiler, which is what makes the
/// first run a real RED rather than a build error.
/// </para>
/// </summary>
public sealed class PersonalIdentityMappingTests
{
    private const string People = "CleanArchitecture.Domain.IdentityAccess.People.";
    private const string ProfileType = People + "PersonProfile";
    private const string OwnershipType = People + "PersonalTenantOwnership";
    private const string DocumentType = People + "IdentityDocument";
    private const string FingerprintType = People + "IdentityDocumentFingerprint";
    private const string BudgetType = "CleanArchitecture.Infrastructure.IdentityAccess.Security.IdentityAttemptBudget";

    private static IModel Model()
    {
        using var scope = TestServices.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().GetService<IDesignTimeModel>().Model;
    }

    private static IEntityType Entity(string name) =>
        Model().FindEntityType(name) ?? throw new AssertionException($"The model does not map {name}.");

    [Test]
    public void Model_keys_a_person_profile_by_its_identity_and_carries_a_row_version()
    {
        var profile = Entity(ProfileType);

        profile.GetTableName().ShouldBe("PersonProfiles");
        profile.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("IdentityId");
        profile.FindProperty("IdentityId")!.GetColumnType().ShouldBe("uuid");
        profile.FindProperty("FullName")!.GetMaxLength().ShouldBe(200);
        profile.FindProperty("DisplayName")!.GetMaxLength().ShouldBe(60);
        profile.FindProperty("Classification")!.GetMaxLength().ShouldBe(32);
        profile.FindProperty("Classification")!.IsNullable.ShouldBeFalse();
        profile.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        profile.FindProperty("Version")!.GetColumnName().ShouldBe("xmin");
        profile.GetForeignKeys()
            .Single(key => key.Properties.Single().Name == "PersonalTenantId")
            .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    [Test]
    public void Model_reserves_one_personal_tenant_per_identity_in_the_database_rather_than_in_a_precheck()
    {
        var ownership = Entity(OwnershipType);

        ownership.GetTableName().ShouldBe("PersonalTenantOwnerships");
        ownership.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("IdentityId");
        ownership.GetIndexes()
            .Single(index => index.Properties.Single().Name == "TenantId")
            .IsUnique.ShouldBeTrue("one Personal tenant belongs to exactly one identity, in both directions");
        ownership.GetForeignKeys()
            .Single(key => key.Properties.Single().Name == "TenantId")
            .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    [Test]
    public void Model_stores_a_document_as_ciphertext_with_no_column_holding_its_digits()
    {
        var document = Entity(DocumentType);

        document.GetTableName().ShouldBe("IdentityDocuments");
        document.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("IdentityId");
        document.FindProperty("Ciphertext")!.GetColumnType().ShouldBe("text");
        document.FindProperty("Country")!.GetMaxLength().ShouldBe(32);
        document.FindProperty("DocumentType")!.GetMaxLength().ShouldBe(32);
        document.FindProperty("Classification")!.GetMaxLength().ShouldBe(32);
        document.FindProperty("PurgedAt")!.IsNullable.ShouldBeTrue();
        document.GetProperties().Select(property => property.Name)
            .ShouldNotContain("DocumentNumber", "a document number never becomes a column");
        document.GetProperties().Select(property => property.Name)
            .ShouldNotContain("DigitCount", "a digit count narrows the search space for a stored number");
    }

    [Test]
    public void Model_makes_a_documentary_identity_unique_across_every_retained_key_version()
    {
        var fingerprint = Entity(FingerprintType);

        fingerprint.GetTableName().ShouldBe("IdentityDocumentFingerprints");
        fingerprint.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .ShouldBe(["IdentityId", "KeyVersion"]);
        fingerprint.FindProperty("Fingerprint")!.GetMaxLength().ShouldBe(64);
        fingerprint.GetIndexes()
            .Single(index => index.Properties.Single().Name == "Fingerprint")
            .IsUnique.ShouldBeTrue("a documentary identity is unique among unpurged rows");
        fingerprint.GetIndexes()
            .Single(index => index.Properties.Single().Name == "Fingerprint")
            .GetDatabaseName().ShouldBe("UX_IdentityDocumentFingerprints_Fingerprint");
    }

    [Test]
    public void Model_holds_the_shared_attempt_budget_in_one_row_per_scope_key_and_window()
    {
        var budget = Entity(BudgetType);

        budget.GetTableName().ShouldBe("IdentityAttemptBudgets");
        budget.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .ShouldBe(["Scope", "KeyHash", "WindowStart"]);
        budget.FindProperty("Count")!.IsNullable.ShouldBeFalse();
        budget.FindProperty("ExpiresAt")!.IsNullable.ShouldBeFalse();
        budget.GetProperties().Select(property => property.Name)
            .ShouldNotContain("Key", "only the hash of a budget key is stored, never the address or identity itself");
    }
}
