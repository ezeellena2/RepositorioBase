using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class InvitationMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// PostgreSQL 18 reports a RESTRICT violation as 23001; every earlier version reports 23503, the same code a
    /// NO ACTION violation always uses. The test host pins no image tag, so both must be accepted and the
    /// constraint name is what actually identifies which foreign key fired.
    /// </summary>
    private static readonly string[] ForeignKeyViolations = [PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation];

    [Test]
    public void Model_maps_invitations_with_uuid_keys_a_tenant_alternate_key_and_an_xmin_concurrency_token()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;
        var invitation = model.FindEntityType(typeof(Invitation));
        var invitationRole = model.FindEntityType(typeof(InvitationRole));

        invitation.ShouldNotBeNull();
        invitationRole.ShouldNotBeNull();
        invitation!.GetTableName().ShouldBe("Invitations");
        invitationRole!.GetTableName().ShouldBe("InvitationRoles");
        invitation.FindPrimaryKey()!.Properties.Single().Name.ShouldBe("Id");
        invitation.FindProperty("Id")!.GetColumnType().ShouldBe("uuid");
        invitation.FindProperty("Id")!.GetValueConverter().ShouldNotBeNull();
        invitation.FindProperty("TenantId")!.GetValueConverter().ShouldNotBeNull();
        invitation.FindProperty(nameof(Invitation.NormalizedEmail))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(Invitation.TokenHash))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(Invitation.ExpiresAt))!.IsNullable.ShouldBeFalse();
        invitation.FindProperty(nameof(Invitation.Status))!.GetMaxLength().ShouldBe(32);
        invitation.FindProperty(nameof(Invitation.AcceptedByIdentityId))!.IsNullable.ShouldBeTrue();
        invitation.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        invitation.FindProperty("Version")!.GetColumnName().ShouldBe("xmin");
        invitation.GetKeys().ShouldContain(key => !key.IsPrimaryKey() && key.Properties.Count == 2 && key.Properties[0].Name == "TenantId" && key.Properties[1].Name == "Id");
        invitation.GetForeignKeys().Single(key => key.Properties.Single().Name == "TenantId").DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        invitation.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(Invitation.AcceptedByIdentityId)).DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
        invitationRole.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["TenantId", "InvitationId", "RoleId"]);
        invitationRole.GetForeignKeys().Single(key => key.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "InvitationId"])).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        invitationRole.GetForeignKeys().Single(key => key.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "RoleId"])).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    [Test]
    public void Model_indexes_the_token_hash_uniquely_and_allows_only_one_pending_invitation_per_recipient()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Invitation));

        invitation.ShouldNotBeNull();
        var tokenHash = invitation!.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(Invitation.TokenHash)]));
        tokenHash.IsUnique.ShouldBeTrue();
        tokenHash.GetFilter().ShouldBeNull("a token hash is unique across every state, not only while pending");

        var pending = invitation.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", nameof(Invitation.NormalizedEmail)]));
        pending.IsUnique.ShouldBeTrue();
        pending.GetFilter().ShouldBe("\"Status\" = 'Pending'");

        invitation.GetIndexes().ShouldContain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { "TenantId", nameof(Invitation.NormalizedEmail), nameof(Invitation.Status) }));
        invitation.GetIndexes().ShouldContain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(Invitation.ExpiresAt) }));
    }

    [Test]
    public async Task Database_rejects_a_cross_tenant_invitation_role_even_when_the_domain_is_bypassed()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, "cross-tenant");
        var other = Tenant.CreateOrganization(TenantSlug.From($"invitation-foreign-{Guid.NewGuid():N}"));
        var foreignRole = Role.Create(other, "Operators");
        context.AddRange(other, foreignRole);
        await context.SaveChangesAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"InvitationRoles\" (\"TenantId\", \"InvitationId\", \"RoleId\") VALUES ({tenant.Id.Value}, {invitation.Id.Value}, {foreignRole.Id.Value})"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.ShouldBe("FK_InvitationRoles_Roles_TenantId_RoleId");
    }

    [Test]
    public async Task Database_rejects_a_second_pending_invitation_for_the_same_recipient()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, first) = await SeedPendingAsync(context, "one-pending");
        var duplicate = Invitation.Issue(tenant, first.NormalizedEmail, [await RoleOfAsync(context, tenant)], VersionedHash(), Now, Now.AddDays(7));

        context.Invitations.Add(duplicate);

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = failure.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("IX_Invitations_TenantId_NormalizedEmail");
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The load-bearing consequence of deriving expiry instead of storing it: the filtered index keys on the
    /// stored status, so a lapsed invitation still occupies its recipient's slot. That is by design — it is what
    /// keeps one row per live recipient without a sweeper — and it obliges the invite use case to reissue or
    /// cancel the existing row rather than issue a second one.
    /// </summary>
    [Test]
    public async Task A_lapsed_invitation_still_holds_its_recipient_slot_and_is_freed_by_reissue_or_cancellation()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, lapsed) = await SeedPendingAsync(context, "lapsed");
        var afterExpiry = Now.AddDays(30);
        lapsed.IsPendingAt(afterExpiry).ShouldBeFalse("the window has closed, but the stored status is still Pending");

        context.Invitations.Add(Invitation.Issue(tenant, lapsed.NormalizedEmail, [await RoleOfAsync(context, tenant)], VersionedHash(), afterExpiry, afterExpiry.AddDays(7)));
        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("IX_Invitations_TenantId_NormalizedEmail");
        context.ChangeTracker.Clear();

        // First escape: rotate the lapsed row in place.
        var reloaded = await context.Invitations.SingleAsync(candidate => candidate.Id == lapsed.Id);
        var tracked = await context.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id);
        reloaded.Reissue(tracked, VersionedHash(), afterExpiry, afterExpiry.AddDays(7));
        await context.SaveChangesAsync();
        reloaded.IsPendingAt(afterExpiry).ShouldBeTrue();
        (await context.Invitations.CountAsync(candidate => candidate.TenantId == tenant.Id && candidate.NormalizedEmail == lapsed.NormalizedEmail)).ShouldBe(1);

        // Second escape: cancelling frees the slot, which is the path a changed role set has to take.
        reloaded.Cancel(tracked, afterExpiry.AddDays(1));
        await context.SaveChangesAsync();
        context.Invitations.Add(Invitation.Issue(tenant, lapsed.NormalizedEmail, [await RoleOfAsync(context, tenant)], VersionedHash(), afterExpiry.AddDays(1), afterExpiry.AddDays(8)));
        await context.SaveChangesAsync();

        (await context.Invitations.CountAsync(candidate => candidate.TenantId == tenant.Id && candidate.NormalizedEmail == lapsed.NormalizedEmail)).ShouldBe(2);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_settled_invitation_never_blocks_a_new_one_for_the_same_recipient(bool accepted)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, settled) = await SeedPendingAsync(context, accepted ? "settled-accepted" : "settled-cancelled");
        if (accepted) settled.Accept(tenant, await SeedIdentityAsync(context), Now.AddDays(1)); else settled.Cancel(tenant, Now.AddDays(1));
        await context.SaveChangesAsync();

        context.Invitations.Add(Invitation.Issue(tenant, settled.NormalizedEmail, [await RoleOfAsync(context, tenant)], VersionedHash(), Now.AddDays(2), Now.AddDays(9)));
        await context.SaveChangesAsync();

        (await context.Invitations.CountAsync(candidate => candidate.TenantId == tenant.Id && candidate.NormalizedEmail == settled.NormalizedEmail)).ShouldBe(2);
    }

    [Test]
    public async Task Reissuing_a_lapsed_invitation_keeps_exactly_one_row_for_its_recipient()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, "reissue");
        var previousHash = invitation.TokenHash;

        invitation.Reissue(tenant, VersionedHash(), Now.AddDays(20), Now.AddDays(27));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persisted = await context.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        persisted.TokenHash.ShouldNotBe(previousHash);
        persisted.ExpiresAt.ShouldBe(Now.AddDays(27));
        (await context.Invitations.CountAsync(candidate => candidate.TenantId == tenant.Id && candidate.NormalizedEmail == invitation.NormalizedEmail)).ShouldBe(1);
        (await context.Invitations.AnyAsync(candidate => candidate.TokenHash == previousHash)).ShouldBeFalse("the previous token no longer resolves to any invitation");
    }

    [Test]
    public async Task Database_rejects_a_duplicate_token_hash()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, first) = await SeedPendingAsync(context, "duplicate-hash");
        var second = Invitation.Issue(tenant, $"other-{Guid.NewGuid():N}@example.test", [await RoleOfAsync(context, tenant)], first.TokenHash, Now, Now.AddDays(7));

        context.Invitations.Add(second);

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = failure.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("IX_Invitations_TokenHash");
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The casing clause of the lifecycle constraint must agree with .NET's invariant case mapping for every
    /// recipient the aggregate accepts. PostgreSQL's locale-aware <c>lower()</c> does not, which is why the
    /// constraint rejects ASCII uppercase instead of comparing against it.
    /// </summary>
    [TestCase("ınfo", TestName = "a Turkish dotless i, which is already canonical")]
    [TestCase("Kelvin", TestName = "a leading U+212A Kelvin sign, which only renders like an ASCII K")]
    [TestCase("StraßE", TestName = "a sharp s beside ASCII uppercase")]
    [TestCase("АННА", TestName = "uppercase Cyrillic, which invariant mapping does lower")]
    public async Task A_recipient_the_aggregate_normalizes_is_always_accepted_by_the_database(string localPart)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-unicode-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        var invitation = Invitation.Issue(tenant, $"{localPart}-{Guid.NewGuid():N}@example.test", [role], VersionedHash(), Now, Now.AddDays(7));

        context.AddRange(tenant, role, invitation);
        await Should.NotThrowAsync(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        (await context.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id)).NormalizedEmail.ShouldBe(invitation.NormalizedEmail);
    }

    /// <summary>
    /// Pins why the aggregate refuses uppercase and titlecase categories outright instead of lowering them: for
    /// this recipient .NET's invariant mapping and PostgreSQL's <c>lower()</c> disagree, so lowering and then
    /// storing would put a value in the column that the constraint rejects.
    /// </summary>
    [Test]
    public async Task The_aggregate_refuses_the_character_on_which_lower_and_invariant_normalization_disagree()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var normalized = "İnfo@example.test".ToLowerInvariant();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT lower(@value) = @value AS agrees, @value !~ '[ABCDEFGHIJKLMNOPQRSTUVWXYZ]' AS accepted;", connection);
        command.Parameters.AddWithValue("value", normalized);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();

        reader.GetBoolean(0).ShouldBeFalse("PostgreSQL lower() disagrees with .NET invariant case mapping here, which is why the aggregate refuses the character instead of lowering it");
        Should.Throw<ArgumentException>(() => Invitation.Issue(
            Tenant.CreateOrganization(TenantSlug.From($"invitation-divergent-{Guid.NewGuid():N}")),
            normalized,
            [Role.Create(Tenant.CreateOrganization(TenantSlug.From($"invitation-divergent-role-{Guid.NewGuid():N}")), "Operators")],
            VersionedHash(),
            Now,
            Now.AddDays(7)));
    }

    /// <summary>
    /// The decomposed spelling of an address the aggregate already composed must be refused by the column, or the
    /// pending-slot index would see two different keys for one recipient.
    /// </summary>
    [Test]
    public async Task Database_rejects_a_recipient_that_is_not_composed()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-nfc-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{tenant.Id.Value}', '{VersionedHash()}', @recipient, 'Pending', NOW(), NOW() + INTERVAL '7 days');",
            connection);
        command.Parameters.AddWithValue("recipient", "josé@example.test");

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_Invitations_Lifecycle");
    }

    [TestCaseSource(nameof(IllegalRows))]
    public async Task Database_rejects_a_row_the_aggregate_could_never_produce(string columns, string values)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-illegal-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();

        // The column list varies per case, so it cannot be parameterised; this mirrors the raw-connection probes
        // MigrationUpgradeTests already uses for constraint assertions.
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", {columns}) VALUES ('{Guid.NewGuid()}', '{tenant.Id.Value}', {values});",
            connection);

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_Invitations_Lifecycle");
    }

    /// <summary>
    /// The canonical form is the thing the pending-slot index is unique over, so two spellings of one recipient
    /// must be unable to coexist even when the aggregate is bypassed entirely.
    /// </summary>
    [Test]
    public async Task Database_rejects_a_recipient_that_is_not_in_its_canonical_form()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-canonical-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        foreach (var recipient in new[] { "АННА@example.test", "Ä@example.test", "ǅ@example.test", "ana surname@example.test", " ana@example.test" })
        {
            await using var command = new NpgsqlCommand(
                $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") " +
                $"VALUES ('{Guid.NewGuid()}', '{tenant.Id.Value}', '{VersionedHash()}', @recipient, 'Pending', NOW(), NOW() + INTERVAL '7 days');",
                connection);
            command.Parameters.AddWithValue("recipient", recipient);

            var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

            failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, $"a non-canonical recipient must never reach the column: {recipient}");
            failure.ConstraintName.ShouldBe("CK_Invitations_Lifecycle");
        }
    }

    /// <summary>
    /// Two spellings of one recipient would otherwise each take a pending slot, because the unique index compares
    /// the stored bytes. The canonical-form clause is what closes that, so it is proven end to end here.
    /// </summary>
    [Test]
    public async Task Two_spellings_of_one_recipient_cannot_both_hold_the_pending_slot()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, "canonical-slot");

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{tenant.Id.Value}', '{VersionedHash()}', @recipient, 'Pending', NOW(), NOW() + INTERVAL '7 days');",
            connection);
        command.Parameters.AddWithValue("recipient", invitation.NormalizedEmail.ToUpperInvariant());

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "the uppercase spelling is refused before it can take a second slot");
    }

    /// <summary>
    /// The column accepts exactly what the hasher emits. A raw token is the same length as a digest, so only the
    /// version tag separates them; anything that is not a 256-bit Base64 digest is refused outright.
    /// </summary>
    [TestCase("v1:raw-secret", TestName = "text that is not a digest")]
    [TestCase("v2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", TestName = "an unsupported hash version")]
    [TestCase("v1:AAAA", TestName = "a digest that is too short")]
    [TestCase("v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", TestName = "a digest that is too long")]
    public async Task Database_rejects_a_token_hash_that_is_not_the_format_the_hasher_emits(string tokenHash)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-hashformat-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{tenant.Id.Value}', @hash, 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days');",
            connection);
        command.Parameters.AddWithValue("hash", tokenHash);

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_Invitations_Lifecycle");
    }

    /// <summary>Whatever the hasher really emits must satisfy the column; this pins the two together.</summary>
    [Test]
    public async Task The_hasher_output_is_exactly_what_the_column_accepts()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-hasherfit-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        var hash = VersionedTokenHash.Of(new SecureTokenGenerator().Generate());
        var invitation = Invitation.Issue(tenant, $"hasher-{Guid.NewGuid():N}@example.test", [role], hash, Now, Now.AddDays(7));

        context.AddRange(tenant, role, invitation);

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [TestCaseSource(nameof(EmptyIdentifierRows))]
    public async Task Database_rejects_an_empty_identifier(string table, string columns, string values)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, "empty-id");
        var role = await RoleOfAsync(context, tenant);

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"{table}\" ({columns}) VALUES ({values.Replace("@tenant", $"'{tenant.Id.Value}'", StringComparison.Ordinal).Replace("@invitation", $"'{invitation.Id.Value}'", StringComparison.Ordinal).Replace("@role", $"'{role.Id.Value}'", StringComparison.Ordinal)});",
            connection);

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBeOneOf("CK_Invitations_Ids_NotEmpty", "CK_InvitationRoles_TenantId_NotEmpty");
    }

    /// <summary>
    /// A row-level check cannot express transition immutability, so the trigger is what actually keeps an
    /// accepted invitation from being restored to Pending by a single statement that bypasses the aggregate.
    /// </summary>
    [Test]
    [TestCase(true, TestName = "an accepted invitation is immutable")]
    [TestCase(false, TestName = "a cancelled invitation is immutable")]
    public async Task A_settled_invitation_cannot_be_changed_by_a_statement_that_bypasses_the_aggregate(bool accepted)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, accepted ? "resurrect-accepted" : "resurrect-cancelled");
        var identityId = await SeedIdentityAsync(context);
        if (accepted) invitation.Accept(tenant, identityId, Now.AddDays(1)); else invitation.Cancel(tenant, Now.AddDays(1));
        await context.SaveChangesAsync();

        // One statement per column, so no part of the guard rides on another. Each assignment has to be a real
        // change for the settled row it targets: setting an already-null column to null changes nothing, and a
        // whole-row comparison correctly lets that through.
        var forbidden = new List<string>
        {
            $"\"TokenHash\" = '{VersionedHash()}'",
            "\"ExpiresAt\" = \"ExpiresAt\" + INTERVAL '1 day'",
            "\"NormalizedEmail\" = 'someone-else@example.test'",
            // Not named in any column list: a whole-row guard freezes the issuance timestamp too.
            "\"CreatedAt\" = \"CreatedAt\" - INTERVAL '1 day'"
        };

        forbidden.Add(accepted
            ? "\"Status\" = 'Pending', \"AcceptedAt\" = NULL, \"AcceptedByIdentityId\" = NULL"
            : "\"Status\" = 'Pending', \"CancelledAt\" = NULL");
        forbidden.Add(accepted ? "\"AcceptedAt\" = \"AcceptedAt\" + INTERVAL '1 hour'" : "\"CancelledAt\" = \"CancelledAt\" + INTERVAL '1 hour'");
        if (accepted) forbidden.Add($"\"AcceptedByIdentityId\" = '{Guid.NewGuid()}'");

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        foreach (var assignment in forbidden)
        {
            await using var command = new NpgsqlCommand($"UPDATE \"Invitations\" SET {assignment} WHERE \"Id\" = '{invitation.Id.Value}';", connection);
            var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            failure.SqlState.ShouldBe(PostgresErrorCodes.RaiseException, $"the trigger must reject: {assignment}");
        }

        context.ChangeTracker.Clear();
        var settled = await context.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        settled.Status.ShouldBe(accepted ? InvitationStatus.Accepted : InvitationStatus.Cancelled);
        settled.AcceptedByIdentityId.ShouldBe(accepted ? identityId : null);
        settled.NormalizedEmail.ShouldBe(invitation.NormalizedEmail);
        settled.TokenHash.ShouldBe(invitation.TokenHash);
    }

    [Test]
    public async Task A_persisted_invitation_never_contains_the_usable_token_in_any_column()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rawToken = new SecureTokenGenerator().Generate();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-secret-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        var invitation = Invitation.Issue(tenant, $"secret-{Guid.NewGuid():N}@example.test", [role], VersionedTokenHash.Of(rawToken), Now, Now.AddDays(7));
        context.AddRange(tenant, role, invitation);
        await context.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT row.*::text FROM \"Invitations\" row WHERE row.\"Id\" = '{invitation.Id.Value}';", connection);
        var persistedRow = (string)(await command.ExecuteScalarAsync())!;

        persistedRow.ShouldContain(invitation.TokenHash.Value, Case.Sensitive, "the row really is being read as text, so the negative assertion below means something");
        persistedRow.ShouldNotContain(rawToken, Case.Insensitive);
        invitation.TokenHash.Matches(rawToken).ShouldBeTrue("only the hash is stored, and it still resolves the token it was made from");
    }

    /// <summary>The database also refuses a raw token written straight past the aggregate.</summary>
    [Test]
    public async Task Database_rejects_a_token_that_is_not_a_versioned_hash()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-rawtoken-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();
        var rawToken = new SecureTokenGenerator().Generate();

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") VALUES ({Guid.NewGuid()}, {tenant.Id.Value}, {rawToken}, {"raw@example.test"}, {"Pending"}, {Now}, {Now.AddDays(7)})"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_Invitations_Lifecycle");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Concurrent_settlement_of_one_invitation_has_a_single_winner(bool loserCancels)
    {
        using var scope = TestServices.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(source, loserCancels ? "winner-accept" : "winner-reissue");
        var identityId = await SeedIdentityAsync(source);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(source.Database.GetConnectionString()).Options;

        await using var winner = new ApplicationDbContext(options);
        await using var loser = new ApplicationDbContext(options);
        var winning = await winner.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        var losing = await loser.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        var winnerTenant = await winner.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id);
        var loserTenant = await loser.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id);

        // Both transactions really overlap: the winner holds the row lock uncommitted while the loser blocks on
        // it, so the loser's concurrency token is re-evaluated against the committed row, not a stale snapshot.
        await using var winnerTransaction = await winner.Database.BeginTransactionAsync();
        winning.Accept(winnerTenant, identityId, Now.AddDays(1));
        await winner.SaveChangesAsync();

        if (loserCancels) losing.Cancel(loserTenant, Now.AddDays(1));
        else losing.Reissue(loserTenant, VersionedHash(), Now.AddDays(1), Now.AddDays(8));
        var blocked = Task.Run(() => loser.SaveChangesAsync());
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        blocked.IsCompleted.ShouldBeFalse("the loser must be waiting on the row lock the winner holds");

        await winnerTransaction.CommitAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () => await blocked);
        await using var verification = new ApplicationDbContext(options);
        var settled = await verification.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        settled.Status.ShouldBe(InvitationStatus.Accepted);
        settled.AcceptedByIdentityId.ShouldBe(identityId);
        settled.CancelledAt.ShouldBeNull();
        settled.TokenHash.ShouldBe(invitation.TokenHash, "the loser's rotation never landed");
    }

    [Test]
    public async Task Deleting_a_tenant_that_still_has_an_invitation_is_restricted()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A role would block the delete through its own tenant key, and creating one through EF also writes an
        // audit row that blocks it again. The invitation is therefore seeded directly, so the only thing left
        // standing between the tenant and deletion is the invitation itself.
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-restrict-tenant-{Guid.NewGuid():N}"));
        context.Add(tenant);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") VALUES ({Guid.NewGuid()}, {tenant.Id.Value}, {VersionedHash().Value}, {$"restrict-{Guid.NewGuid():N}@example.test"}, {"Pending"}, {Now}, {Now.AddDays(7)})");

        var failure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"DELETE FROM \"Tenants\" WHERE \"Id\" = {tenant.Id.Value}"));

        failure.ConstraintName.ShouldBe("FK_Invitations_Tenants_TenantId", "invitation history must survive its tenant, never cascade away with it");
        failure.SqlState.ShouldBeOneOf(ForeignKeyViolations);
    }

    [Test]
    public async Task Deleting_the_accepting_identity_or_an_offered_role_is_restricted()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenant, invitation) = await SeedPendingAsync(context, "restrict-role");
        var role = await RoleOfAsync(context, tenant);
        var identityId = await SeedIdentityAsync(context);
        invitation.Accept(tenant, identityId, Now.AddDays(1));
        await context.SaveChangesAsync();

        var roleFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"DELETE FROM \"Roles\" WHERE \"Id\" = {role.Id.Value}"));
        roleFailure.ConstraintName.ShouldBe("FK_InvitationRoles_Roles_TenantId_RoleId");
        roleFailure.SqlState.ShouldBeOneOf(ForeignKeyViolations);

        var identityFailure = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = {identityId}"));
        identityFailure.ConstraintName.ShouldBe("FK_Invitations_AspNetUsers_AcceptedByIdentityId");
        identityFailure.SqlState.ShouldBeOneOf(ForeignKeyViolations);
    }

    private static IEnumerable<TestCaseData> IllegalRows()
    {
        const string terminal = "\"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\"";
        const string acceptance = ", \"AcceptedByIdentityId\", \"AcceptedAt\"";
        var hash = VersionedHash();
        yield return new TestCaseData(terminal, $"'{hash}', '', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("an empty recipient");
        yield return new TestCaseData(terminal, $"'{hash}', 'Ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("a recipient carrying uppercase");
        yield return new TestCaseData(terminal, $"'{hash}', ' ana@example.test ', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("an untrimmed recipient");
        yield return new TestCaseData(terminal, $"'{hash}', 'not-an-address', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("a recipient that is not an address");
        yield return new TestCaseData(terminal, $"'', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("an empty token hash");
        yield return new TestCaseData(terminal, $"'not-versioned', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("a token hash that is not versioned");
        yield return new TestCaseData(terminal, $"'{hash}', 'ana@example.test', 'Pending', NOW(), NOW()").SetName("an expiry that does not outlive its creation");
        yield return new TestCaseData(terminal, $"'{hash}', 'ana@example.test', 'Expired', NOW(), NOW() + INTERVAL '7 days'").SetName("a status the enum does not define");
        yield return new TestCaseData(terminal, $"'{hash}', 'ana@example.test', 'pending', NOW(), NOW() + INTERVAL '7 days'").SetName("a mis-cased status");
        yield return new TestCaseData(terminal + acceptance, $"'{hash}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}', NOW()").SetName("a pending invitation carrying an acceptance");
        yield return new TestCaseData(terminal + ", \"CancelledAt\"", $"'{hash}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days', NOW()").SetName("a pending invitation carrying a cancellation");
        yield return new TestCaseData(terminal + acceptance + ", \"CancelledAt\"", $"'{hash}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}', NOW(), NOW()").SetName("an accepted invitation carrying a cancellation");
        yield return new TestCaseData(terminal, $"'{hash}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days'").SetName("an accepted invitation without its acceptance evidence");
        yield return new TestCaseData(terminal + acceptance, $"'{hash}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}', NOW() + INTERVAL '8 days'").SetName("an acceptance after the invitation expired");
        yield return new TestCaseData(terminal, $"'{hash}', 'ana@example.test', 'Cancelled', NOW(), NOW() + INTERVAL '7 days'").SetName("a cancelled invitation without its cancellation instant");
        yield return new TestCaseData(terminal + acceptance + ", \"CancelledAt\"", $"'{hash}', 'ana@example.test', 'Cancelled', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}', NOW(), NOW()").SetName("a cancelled invitation carrying an acceptance");

        // Each status branch pairs "who accepted" with "when", and a case that supplies or omits both at once
        // leaves either half of the pair unpinned: delete one clause and the other still rejects the row. These
        // six split every pair so each of the constraint's clauses is exercised on its own.
        const string acceptor = ", \"AcceptedByIdentityId\"";
        const string acceptedAt = ", \"AcceptedAt\"";
        yield return new TestCaseData(terminal + acceptor, $"'{hash}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}'").SetName("a pending invitation naming an acceptor");
        yield return new TestCaseData(terminal + acceptedAt, $"'{hash}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days', NOW()").SetName("a pending invitation carrying an acceptance instant");
        yield return new TestCaseData(terminal + acceptedAt, $"'{hash}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days', NOW()").SetName("an accepted invitation with no acceptor");
        yield return new TestCaseData(terminal + acceptor, $"'{hash}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days', '{Guid.NewGuid()}'").SetName("an accepted invitation with no acceptance instant");
        yield return new TestCaseData(terminal + ", \"CancelledAt\"" + acceptor, $"'{hash}', 'ana@example.test', 'Cancelled', NOW(), NOW() + INTERVAL '7 days', NOW(), '{Guid.NewGuid()}'").SetName("a cancelled invitation naming an acceptor");
        yield return new TestCaseData(terminal + ", \"CancelledAt\"" + acceptedAt, $"'{hash}', 'ana@example.test', 'Cancelled', NOW(), NOW() + INTERVAL '7 days', NOW(), NOW()").SetName("a cancelled invitation carrying an acceptance instant");
    }

    private static IEnumerable<TestCaseData> EmptyIdentifierRows()
    {
        const string empty = "'00000000-0000-0000-0000-000000000000'";
        yield return new TestCaseData(
            "Invitations",
            "\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\"",
            $"{empty}, @tenant, '{VersionedHash()}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("an empty invitation identifier");
        yield return new TestCaseData(
            "Invitations",
            "\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\"",
            $"'{Guid.NewGuid()}', {empty}, '{VersionedHash()}', 'ana@example.test', 'Pending', NOW(), NOW() + INTERVAL '7 days'").SetName("an empty tenant on an invitation");
        yield return new TestCaseData(
            "Invitations",
            "\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\", \"AcceptedByIdentityId\", \"AcceptedAt\"",
            $"'{Guid.NewGuid()}', @tenant, '{VersionedHash()}', 'ana@example.test', 'Accepted', NOW(), NOW() + INTERVAL '7 days', {empty}, NOW()").SetName("an empty accepting identity");
        yield return new TestCaseData(
            "InvitationRoles",
            "\"TenantId\", \"InvitationId\", \"RoleId\"",
            $"{empty}, @invitation, @role").SetName("an empty tenant on an offered role");
    }

    /// <summary>A distinct hash per call. Interpolating it yields the stored value, so raw SQL keeps working.</summary>
    private static VersionedTokenHash VersionedHash() => VersionedTokenHash.Of(Guid.NewGuid().ToString());

    private static async Task<Role> RoleOfAsync(ApplicationDbContext context, Tenant tenant) =>
        await context.TenantRoles.FirstAsync(candidate => candidate.TenantId == tenant.Id);

    private static async Task<Guid> SeedIdentityAsync(ApplicationDbContext context)
    {
        var identityId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser { Id = identityId, UserName = $"invitee-{identityId:N}", Email = $"invitee-{identityId:N}@test.invalid" });
        await context.SaveChangesAsync();
        return identityId;
    }

    private static async Task<(Tenant Tenant, Invitation Invitation)> SeedPendingAsync(ApplicationDbContext context, string label)
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitation-{label}-{Guid.NewGuid():N}"));
        var role = Role.Create(tenant, "Operators");
        var invitation = Invitation.Issue(tenant, $"{label}-{Guid.NewGuid():N}@example.test", [role], VersionedHash(), Now, Now.AddDays(7));
        context.AddRange(tenant, role, invitation);
        await context.SaveChangesAsync();
        return (tenant, invitation);
    }
}
