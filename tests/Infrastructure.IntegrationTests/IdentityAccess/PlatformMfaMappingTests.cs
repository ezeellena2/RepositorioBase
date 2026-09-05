using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What PostgreSQL itself holds true about the Platform aggregates. The invitation comes first: MFA binds to it,
/// so an enrollment cannot be trusted before the invitation it is bound to can be stored (Task 14 Steps 1 and 3).
/// </summary>
public sealed class PlatformMfaMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static VersionedTokenHash NextHash() => VersionedTokenHash.Of(Guid.NewGuid().ToString("N"));

    private static async Task<Tenant> PlatformAsync(ApplicationDbContext context)
    {
        var existing = await context.Tenants.FirstOrDefaultAsync(tenant => tenant.Type == TenantType.Platform);
        if (existing is not null) return existing;
        var platform = Tenant.CreatePlatform();
        platform.Activate();
        context.Add(platform);
        await context.SaveChangesAsync();
        return platform;
    }

    [Test]
    public void Model_maps_platform_invitations_with_uuid_keys_and_an_xmin_concurrency_token()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlatformAdminInvitation));

        invitation.ShouldNotBeNull();
        invitation!.GetTableName().ShouldBe("PlatformAdminInvitations");
        invitation.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("Id");
        invitation.FindProperty("Id")!.GetColumnType().ShouldBe("uuid");
        invitation.FindProperty("Id")!.GetValueConverter().ShouldNotBeNull();
        invitation.FindProperty("TenantId")!.GetValueConverter().ShouldNotBeNull();
        invitation.FindProperty(nameof(PlatformAdminInvitation.NormalizedEmail))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(PlatformAdminInvitation.TokenHash))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(PlatformAdminInvitation.ExpiresAt))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(PlatformAdminInvitation.Status))!.GetMaxLength().ShouldBe(32);
        invitation.FindProperty(nameof(PlatformAdminInvitation.Delivery))!.GetMaxLength().ShouldBe(32);
        invitation.FindProperty(nameof(PlatformAdminInvitation.BoundIdentityId))!.IsNullable.ShouldBeTrue();
        invitation.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        invitation.FindProperty("Version")!.GetColumnName().ShouldBe("xmin");
        invitation.GetForeignKeys().Single(key => key.Properties.Single().Name == "TenantId").DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        invitation.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(PlatformAdminInvitation.BoundIdentityId)).DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
    }

    [Test]
    public void Model_indexes_the_token_hash_uniquely_and_allows_only_one_pending_invitation_per_recipient()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlatformAdminInvitation));

        invitation.ShouldNotBeNull();
        var tokenHash = invitation!.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(PlatformAdminInvitation.TokenHash)]));
        tokenHash.IsUnique.ShouldBeTrue();
        tokenHash.GetFilter().ShouldBeNull("a token hash is unique across every state.");

        var pending = invitation.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(PlatformAdminInvitation.NormalizedEmail)]));
        pending.IsUnique.ShouldBeTrue();
        pending.GetFilter().ShouldBe("\"Status\" = 'Pending'");
    }

    /// <summary>
    /// Exactly one Platform tenant may exist (IA-REQ-039). The slug is already unique, and the Platform slug is
    /// reserved, so the singleton is held by the database rather than only by the code that creates it.
    /// </summary>
    [Test]
    public async Task Database_rejects_a_second_platform_tenant()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await PlatformAsync(context);

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"Tenants\" (\"Id\", \"Type\", \"Status\", \"Slug\", \"AuthorizationVersion\") VALUES (gen_random_uuid(), 'Platform', 'Active', 'platform', 0)"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task Database_rejects_a_second_pending_invitation_for_the_same_recipient()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platform = await PlatformAsync(context);
        var recipient = $"owner-{Guid.NewGuid():N}@example.test";
        context.Add(PlatformAdminInvitation.Issue(platform, recipient, NextHash(), true, Now, Now.AddDays(7)));
        await context.SaveChangesAsync();

        context.Add(PlatformAdminInvitation.Issue(platform, recipient, NextHash(), false, Now, Now.AddDays(7)));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The lifecycle check restates the aggregate's invariants where the database enforces them too, so a row
    /// written around the domain cannot claim an acceptance with nobody bound to it.
    /// </summary>
    [Test]
    public async Task Database_rejects_an_accepted_invitation_with_no_bound_identity()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platform = await PlatformAsync(context);
        var hash = NextHash();

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "PlatformAdminInvitations"
               ("Id", "TenantId", "NormalizedEmail", "TokenHash", "Status", "Delivery", "IsOwner", "CreatedAt", "ExpiresAt", "AcceptedAt")
             VALUES (gen_random_uuid(), {platform.Id.Value}, {$"forged-{Guid.NewGuid():N}@example.test"}, {hash.Value},
                     'Accepted', 'Pending', FALSE, {Now}, {Now.AddDays(7)}, {Now})
             """));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_PlatformAdminInvitations_Lifecycle");
    }

    [Test]
    public async Task Database_rejects_a_token_hash_that_is_not_the_format_the_hasher_emits()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platform = await PlatformAsync(context);

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "PlatformAdminInvitations"
               ("Id", "TenantId", "NormalizedEmail", "TokenHash", "Status", "Delivery", "IsOwner", "CreatedAt", "ExpiresAt")
             VALUES (gen_random_uuid(), {platform.Id.Value}, {$"forged-{Guid.NewGuid():N}@example.test"}, 'not-a-hash',
                     'Pending', 'Pending', FALSE, {Now}, {Now.AddDays(7)})
             """));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_PlatformAdminInvitations_Lifecycle");
    }
    [Test]
    public void Model_maps_one_enrollment_per_identity_with_hashed_recovery_codes()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;
        var enrollment = model.FindEntityType(typeof(PlatformMfaEnrollment)).ShouldNotBeNull();
        var code = model.FindEntityType(typeof(PlatformRecoveryCode)).ShouldNotBeNull();

        enrollment.GetTableName().ShouldBe("PlatformMfaEnrollments");
        code.GetTableName().ShouldBe("PlatformRecoveryCodes");
        enrollment.FindProperty(nameof(PlatformMfaEnrollment.EncryptedSecret))!.IsNullable.ShouldBeFalse();
        enrollment.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        enrollment.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(PlatformMfaEnrollment.IdentityId)]))
            .IsUnique.ShouldBeTrue("one second factor per identity.");
        code.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(PlatformRecoveryCode.EnrollmentId), nameof(PlatformRecoveryCode.CodeHash)]))
            .IsUnique.ShouldBeTrue();
    }

    /// <summary>The stored secret is ciphertext; a plaintext TOTP secret must never reach the column.</summary>
    [Test]
    public async Task An_enrollment_stores_only_the_ciphertext_it_was_given()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityId = await SeedIdentityAsync(context);
        context.Add(PlatformMfaEnrollment.Begin(identityId, "protected-ciphertext", ["code-hash-1"], Now));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.PlatformMfaEnrollments.SingleAsync(item => item.IdentityId == identityId);
        stored.EncryptedSecret.ShouldBe("protected-ciphertext");
        stored.Status.ShouldBe(PlatformMfaEnrollmentStatus.Pending);
    }

    [Test]
    public async Task Database_rejects_a_second_enrollment_for_the_same_identity()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityId = await SeedIdentityAsync(context);
        context.Add(PlatformMfaEnrollment.Begin(identityId, "ciphertext", ["code-hash-1"], Now));
        await context.SaveChangesAsync();

        context.Add(PlatformMfaEnrollment.Begin(identityId, "other", ["code-hash-2"], Now));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        context.ChangeTracker.Clear();
    }

    /// <summary>An active enrollment that was never acknowledged would be authority without its last gate.</summary>
    [Test]
    public async Task Database_rejects_an_active_enrollment_that_was_never_acknowledged()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityId = await SeedIdentityAsync(context);

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "PlatformMfaEnrollments"
               ("Id", "IdentityId", "EncryptedSecret", "Status", "CreatedAt", "VerifiedAt")
             VALUES (gen_random_uuid(), {identityId}, 'ciphertext', 'Active', {Now}, {Now})
             """));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_PlatformMfaEnrollments_Lifecycle");
    }

    private static async Task<Guid> SeedIdentityAsync(ApplicationDbContext context)
    {
        var identityId = Guid.NewGuid();
        var email = $"mfa-{identityId:N}@example.test";
        await context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
                                        "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
             VALUES ({identityId}, {email}, {email.ToUpperInvariant()}, {email}, {email.ToUpperInvariant()}, TRUE,
                     {Guid.NewGuid().ToString("N")}, {Guid.NewGuid().ToString("N")}, FALSE, FALSE, TRUE, 0)
             """);
        return identityId;
    }
}