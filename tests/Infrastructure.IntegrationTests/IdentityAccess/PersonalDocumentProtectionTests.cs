using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess.People;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What protects a person's documentary identity, and what PostgreSQL refuses once it is recorded (IA-REQ-050).
/// <para>
/// This project shares one live database and resets nothing, so every test cleans up exactly the rows it created.
/// </para>
/// </summary>
public sealed class PersonalDocumentProtectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly List<Guid> _identityIds = [];
    private readonly List<TenantId> _tenantIds = [];

    private static string Digits() => Random.Shared.Next(1_000_000, 9_999_999).ToString() + Random.Shared.Next(0, 9);

    private static NormalizedDocument Document(string number) =>
        NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, number);

    private static IdentityDocumentProtectionOptions TwoRetainedKeys() => new()
    {
        CurrentKeyVersion = 2,
        FingerprintKeys = new Dictionary<int, string>
        {
            [1] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)index).ToArray()),
            [2] = Convert.ToBase64String(Enumerable.Range(32, 32).Select(index => (byte)index).ToArray())
        }
    };

    private static IIdentityDocumentFingerprint Fingerprints(IdentityDocumentProtectionOptions? options = null) =>
        new IdentityDocumentFingerprintFactory(Options.Create(options ?? TwoRetainedKeys()));

    private async Task<(Guid IdentityId, Tenant Personal)> SeedPersonAsync(ApplicationDbContext context)
    {
        var identityId = Guid.NewGuid();
        var email = $"person-{identityId:N}@example.test";
        context.Users.Add(new ApplicationUser
        {
            Id = identityId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        });

        var personal = Tenant.CreatePersonal(TenantSlug.From($"personal-{identityId:N}"));
        personal.Activate();
        context.Tenants.Add(personal);
        context.PersonalTenantOwnerships.Add(PersonalTenantOwnership.Create(personal, identityId));
        context.PersonProfiles.Add(PersonProfile.Create(personal, identityId, "Jane Doe", "Jane", DataClassification.Synthetic));
        await context.SaveChangesAsync();

        _identityIds.Add(identityId);
        _tenantIds.Add(personal.Id);
        return (identityId, personal);
    }

    [TearDown]
    public async Task Remove_only_this_tests_rows()
    {
        if (_identityIds.Count == 0 && _tenantIds.Count == 0) return;

        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.IdentityDocuments.Where(document => _identityIds.Contains(document.IdentityId)).ExecuteDeleteAsync();
        await context.PersonProfiles.Where(profile => _identityIds.Contains(profile.IdentityId)).ExecuteDeleteAsync();
        await context.PersonalTenantOwnerships.Where(ownership => _identityIds.Contains(ownership.IdentityId)).ExecuteDeleteAsync();
        await context.Tenants.Where(tenant => _tenantIds.Contains(tenant.Id)).ExecuteDeleteAsync();
        await context.Users.Where(user => _identityIds.Contains(user.Id)).ExecuteDeleteAsync();
        _identityIds.Clear();
        _tenantIds.Clear();
    }

    [Test]
    public void A_document_round_trips_through_the_deployment_key_and_nothing_else()
    {
        using var scope = TestServices.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var document = Document("12.345.678");

        var ciphertext = protector.Protect(document);

        ciphertext.ShouldNotContain("12345678");
        protector.Reveal(ciphertext).ShouldBe(document);
    }

    [Test]
    public void A_payload_sealed_for_another_purpose_is_refused_rather_than_read()
    {
        using var scope = TestServices.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var foreign = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("identity-access.registration.outbox-secret.v1")
            .Protect("AR|DNI|12345678");

        protector.Reveal(foreign).ShouldBeNull("purpose separation is what stops one envelope opening another's payload");
        protector.Reveal("not-even-a-payload").ShouldBeNull();
    }

    [Test]
    public void Two_spellings_of_one_document_reach_one_fingerprint()
    {
        var fingerprints = Fingerprints();

        var typed = fingerprints.ForRetainedKeys(Document(" 12.345.678 "));
        var plain = fingerprints.ForRetainedKeys(Document("12345678"));

        typed.Select(value => value.Value).ShouldBe(plain.Select(value => value.Value));
    }

    [Test]
    public void A_fingerprint_is_produced_for_every_retained_key_version()
    {
        var fingerprints = Fingerprints();

        var values = fingerprints.ForRetainedKeys(Document("12345678"));

        fingerprints.CurrentKeyVersion.ShouldBe(2);
        values.Select(value => value.KeyVersion).ShouldBe([2, 1], "the current version leads, so a new row is written under it");
        values.Select(value => value.Value).Distinct().Count().ShouldBe(2, "different keys must not produce the same digest");
        values.ShouldAllBe(value => value.Value.StartsWith($"k{value.KeyVersion}:v1:", StringComparison.Ordinal));
    }

    [Test]
    public void Protection_material_that_is_absent_default_or_unversioned_is_refused()
    {
        var absent = new IdentityDocumentProtectionOptions { CurrentKeyVersion = 1, FingerprintKeys = [] };
        Should.Throw<InvalidOperationException>(() => Fingerprints(absent).ForRetainedKeys(Document("12345678")));

        var tooShort = new IdentityDocumentProtectionOptions
        {
            CurrentKeyVersion = 1,
            FingerprintKeys = new Dictionary<int, string> { [1] = Convert.ToBase64String(new byte[8]) }
        };
        Should.Throw<InvalidOperationException>(() => Fingerprints(tooShort).ForRetainedKeys(Document("12345678")));

        var notAKey = new IdentityDocumentProtectionOptions
        {
            CurrentKeyVersion = 1,
            FingerprintKeys = new Dictionary<int, string> { [1] = "not base64 at all" }
        };
        Should.Throw<InvalidOperationException>(() => Fingerprints(notAKey).ForRetainedKeys(Document("12345678")));

        var unversioned = new IdentityDocumentProtectionOptions
        {
            CurrentKeyVersion = 0,
            FingerprintKeys = new Dictionary<int, string> { [0] = Convert.ToBase64String(new byte[32]) }
        };
        Should.Throw<InvalidOperationException>(() => Fingerprints(unversioned).ForRetainedKeys(Document("12345678")));
    }

    [Test]
    public async Task One_documentary_identity_can_be_recorded_once_across_every_retained_key_version()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var fingerprints = Fingerprints();
        var number = Digits();

        var (first, _) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            first, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));
        await context.SaveChangesAsync();

        var (second, _) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            second, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        context.ChangeTracker.Clear();
    }

    [Test]
    public async Task Suspending_the_personal_tenant_does_not_free_the_document()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var fingerprints = Fingerprints();
        var number = Digits();

        var (first, personal) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            first, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));
        await context.SaveChangesAsync();

        personal.Suspend(TenantSuspensionReason.PolicyViolation, Now);
        await context.SaveChangesAsync();

        var (second, _) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            second, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        context.ChangeTracker.Clear();
    }

    [Test]
    public async Task Only_a_purge_makes_a_number_reclaimable()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var fingerprints = Fingerprints();
        var number = Digits();

        var (first, _) = await SeedPersonAsync(context);
        var recorded = IdentityDocument.Record(
            first, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now);
        context.IdentityDocuments.Add(recorded);
        await context.SaveChangesAsync();

        recorded.Purge(Now.AddDays(1));
        await context.SaveChangesAsync();

        (await context.IdentityDocumentFingerprints.CountAsync(fingerprint => fingerprint.IdentityId == first))
            .ShouldBe(0, "a purged number stops occupying the index it was found by");

        var (second, _) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            second, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));

        await context.SaveChangesAsync();
        (await context.IdentityDocuments.CountAsync(document => document.IdentityId == second)).ShouldBe(1);
    }

    [Test]
    public async Task A_failure_after_the_profile_is_written_leaves_no_half_built_personal_context()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var fingerprints = Fingerprints();
        var number = Digits();

        var (owner, _) = await SeedPersonAsync(context);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            owner, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));
        await context.SaveChangesAsync();

        var latecomerId = Guid.NewGuid();
        var email = $"person-{latecomerId:N}@example.test";
        var personal = Tenant.CreatePersonal(TenantSlug.From($"personal-{latecomerId:N}"));
        personal.Activate();

        // The provider retries transient failures, so a hand-rolled transaction has to be executed as one retriable
        // unit — the same reason production code goes through IApplicationTransaction rather than BeginTransaction.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Users.Add(new ApplicationUser
            {
                Id = latecomerId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
            context.Tenants.Add(personal);
            context.PersonalTenantOwnerships.Add(PersonalTenantOwnership.Create(personal, latecomerId));
            context.PersonProfiles.Add(PersonProfile.Create(personal, latecomerId, "Late Comer", "Late", DataClassification.Synthetic));
            await context.SaveChangesAsync();

            context.IdentityDocuments.Add(IdentityDocument.Record(
                latecomerId, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
                protector.Protect(Document(number)), fingerprints.ForRetainedKeys(Document(number)), DataClassification.Synthetic, Now));
            await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
            await transaction.RollbackAsync();
        });

        context.ChangeTracker.Clear();
        (await context.PersonProfiles.CountAsync(profile => profile.IdentityId == latecomerId)).ShouldBe(0);
        (await context.PersonalTenantOwnerships.CountAsync(ownership => ownership.IdentityId == latecomerId)).ShouldBe(0);
        (await context.Tenants.CountAsync(tenant => tenant.Id == personal.Id)).ShouldBe(0);
        (await context.Users.CountAsync(user => user.Id == latecomerId)).ShouldBe(0);
        (await context.IdentityDocuments.CountAsync(document => document.IdentityId == owner))
            .ShouldBe(1, "the failed claim leaves the person who already held that number untouched");
    }
}
