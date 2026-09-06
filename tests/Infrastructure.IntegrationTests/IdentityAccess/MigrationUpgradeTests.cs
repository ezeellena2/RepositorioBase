using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class MigrationUpgradeTests
{
    private const string BaselineMigration = "20260831183429_BaselinePostgreSql";
    private const string RegistrationMessagingPredecessor = "20260901024500_AuthorizationDenialAudit";
    private const string UserSessionsPredecessor = "20260901050000_RegistrationMessaging";
    private const string InvitationsPredecessor = "20260901184054_UserSessions";
    private const string CanonicalFormPredecessor = "20260903170351_Invitations";
    private const string PlatformInvitationPredecessor = "20260904231855_OutboxDeliverySafety";
    private const string PlatformMfaPredecessor = "20260905143153_PlatformAdminInvitation";
    private const string PersonalIdentityPredecessor = "20260906054156_DeferredRegistrationReservation";
    private const string SharedBudgetPredecessor = "20260906145245_PersonalIdentity";

    [Test]
    public async Task Outbox_safety_upgrade_preserves_history_and_bounds_preexisting_attempts_conservatively()
    {
        var databaseName = $"outbox_safety_upgrade_{Guid.NewGuid():N}";
        string? connectionString = null;
        try
        {
            using var scope = TestServices.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
            connectionString = new NpgsqlConnectionStringBuilder(source) { Database = databaseName }.ConnectionString;
            await CreateDatabase(databaseName, source);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            await using var context = new ApplicationDbContext(options);
            await context.Database.GetService<IMigrator>().MigrateAsync("20260904210617_OutboxDispatchState");
            var messageId = Guid.NewGuid();
            var created = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO outbox_messages ("Id", "Type", "Payload", "AttemptCount", "NextAttemptAt", "CreatedAt", "Status", "Generation", "FailureCode")
                VALUES ({{messageId}}, 'identity.invitation.requested', '{}', 3, {{created}}, {{created}}, 'Pending', 3, 'provider_error');
                """);
            await context.Database.GetService<IMigrator>().MigrateAsync();
            var preserved = await context.OutboxMessages.SingleAsync(item => item.Id == messageId);
            preserved.CreatedAt.ShouldBe(created);
            preserved.FirstAttemptAt.ShouldBe(created, "an older ambiguous attempt cannot acquire a fresh 24-hour window on upgrade");
            preserved.AttemptCount.ShouldBe(3);
            preserved.Generation.ShouldBe(3);
            preserved.FailureCode.ShouldBe("provider_error");
            preserved.RequestFingerprint.ShouldBeNull();
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally { if (connectionString is not null) await DropDatabase(databaseName, connectionString); }
    }

    [Test]
    public async Task TenantAuthorization_empty_database_upgrades_to_latest_without_pending_migrations()
    {
        var databaseName = $"tenant_authorization_empty_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            await using var context = new ApplicationDbContext(options);
            await context.Database.GetService<IMigrator>().MigrateAsync();

            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            await AssertSchema(connectionString!);
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task IdentityAccess_upgrade_preserves_baseline_identity_and_todo_data()
    {
        var databaseName = $"identity_access_upgrade_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var todoId = await SeedBaselineData(options, userId, roleId);

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await upgradedContext.Database.GetService<IMigrator>().MigrateAsync();

                (await upgradedContext.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("UPGRADE@EXAMPLE.TEST");
                (await upgradedContext.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);
                (await upgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            }

            await AssertSchema(connectionString!);
            await AssertDowngrade(options, userId, todoId);
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task RegistrationMessaging_round_trip_preserves_preexisting_sentinels_and_removes_only_task_seven_schema()
    {
        var databaseName = $"registration_messaging_round_trip_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var todoId = await SeedPreRegistrationMessagingData(options, userId, roleId);

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await upgradedContext.Database.GetService<IMigrator>().MigrateAsync();
                (await upgradedContext.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
                (await upgradedContext.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);
                (await upgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            }

            await AssertSchema(connectionString!);
            await AssertAuditTriggerRejectsMutation(connectionString!);

            await using (var downgradedContext = new ApplicationDbContext(options))
            {
                await downgradedContext.Database.GetService<IMigrator>().MigrateAsync(RegistrationMessagingPredecessor);
            }

            await AssertTaskSevenSchemaIsAbsent(connectionString!);
            await AssertPreexistingSentinelsAndAuditTrigger(connectionString!, userId, todoId);

            await using var reupgradedContext = new ApplicationDbContext(options);
            await reupgradedContext.Database.GetService<IMigrator>().MigrateAsync();
            (await reupgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task UserSessions_round_trip_preserves_preexisting_sentinels_and_removes_only_session_schema()
    {
        var databaseName = $"user_sessions_round_trip_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var todoId = await SeedPreRegistrationMessagingData(options, userId, roleId);

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await upgradedContext.Database.GetService<IMigrator>().MigrateAsync();
                (await upgradedContext.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
                (await upgradedContext.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);
            }

            await AssertUserSessionSchema(connectionString!);
            await AssertAuditTriggerRejectsMutation(connectionString!);

            await using (var downgradedContext = new ApplicationDbContext(options))
            {
                await downgradedContext.Database.GetService<IMigrator>().MigrateAsync(UserSessionsPredecessor);
            }

            await AssertUserSessionSchemaIsAbsent(connectionString!);
            await AssertPreexistingSentinelsAndAuditTrigger(connectionString!, userId, todoId);

            await using var reupgradedContext = new ApplicationDbContext(options);
            await reupgradedContext.Database.GetService<IMigrator>().MigrateAsync();
            (await reupgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task Invitations_round_trip_preserves_preexisting_sentinels_and_removes_only_invitation_schema()
    {
        var databaseName = $"invitations_round_trip_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var todoId = await SeedPreInvitationData(options, userId, roleId);

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await upgradedContext.Database.GetService<IMigrator>().MigrateAsync();
                (await upgradedContext.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
                (await upgradedContext.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);
                (await upgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                await SeedInvitation(upgradedContext);
            }

            await AssertInvitationSchema(connectionString!);
            await AssertAuditTriggerRejectsMutation(connectionString!);

            await using (var downgradedContext = new ApplicationDbContext(options))
            {
                // The invitation rows seeded above are still present, so this also proves Down survives data.
                await downgradedContext.Database.GetService<IMigrator>().MigrateAsync(InvitationsPredecessor);
            }

            await AssertInvitationSchemaIsAbsent(connectionString!);
            await AssertPreexistingSentinelsAndAuditTrigger(connectionString!, userId, todoId);

            await using var reupgradedContext = new ApplicationDbContext(options);
            await reupgradedContext.Database.GetService<IMigrator>().MigrateAsync();
            (await reupgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    /// <summary>
    /// The canonical-form migration tightens a constraint an applied database has already been enforcing, so the
    /// rows that database is holding were valid under the looser rule and may not be valid under the tighter one.
    /// A constraint added over such rows fails, and the deployment fails with it. This seeds exactly those rows
    /// and requires the upgrade to carry them across.
    /// </summary>
    [Test]
    public async Task InvitationCanonicalForm_upgrades_a_database_holding_rows_the_previous_constraint_allowed()
    {
        var databaseName = $"invitation_canonical_upgrade_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var tenantId = Guid.NewGuid();
            await using (var seedContext = new ApplicationDbContext(options))
            {
                await seedContext.Database.GetService<IMigrator>().MigrateAsync(CanonicalFormPredecessor);
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await Execute(connection, $"INSERT INTO \"Tenants\" (\"Id\", \"Type\", \"Status\", \"Slug\", \"AuthorizationVersion\") VALUES ('{tenantId}', 'Organization', 'Active', 'canonical-upgrade-{tenantId:N}', 0);");

                // Every one of these satisfied the constraint this migration replaces: it pinned only the "v<n>:"
                // prefix, and said nothing about composition, casing or whitespace.
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "ana@example.test");
                await SeedLegacyInvitation(connection, tenantId, "v7:anything-at-all", "bruno@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Convert.ToBase64String(Guid.NewGuid().ToByteArray())}", "carla@example.test"); // A "v1:" prefix over 16 bytes: the shape of a digest without being one.
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "АННА@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "josé@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "dario @example.test");
            }

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await Should.NotThrowAsync(
                    () => upgradedContext.Database.GetService<IMigrator>().MigrateAsync(),
                    "an applied database must be able to reach the tightened constraint.");
                (await upgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Not merely "the migration ran": every row has one defined destination. A recipient the
                // application can still address is repaired into canonical form and stays pending. A row whose
                // digest the application cannot read is retired instead, because no token could ever be checked
                // against it — that covers both the unknown version and the "v1:" prefix over something that was
                // never a SHA-256 digest, which is precisely what the old prefix-only rule let through.
                var rows = new List<string>();
                await using (var command = new NpgsqlCommand($"SELECT \"NormalizedEmail\" || ' ' || \"Status\" FROM \"Invitations\" WHERE \"TenantId\" = '{tenantId}' ORDER BY 1;", connection))
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        rows.Add(reader.GetString(0));
                    }
                }

                rows.ShouldBe(
                    [
                        "ana@example.test Pending",
                        "bruno@example.test Cancelled",
                        "carla@example.test Cancelled",
                        "dario@example.test Pending",
                        "josé@example.test Pending".Normalize(NormalizationForm.FormC).Normalize(NormalizationForm.FormC),
                        "анна@example.test Pending"
                    ],
                    ignoreOrder: true,
                    "invitation history survives the upgrade; a row is repaired or retired, never deleted.");

                (await Scalar<long>(connection, $"SELECT count(*) FROM \"Invitations\" WHERE \"TenantId\" = '{tenantId}' AND \"TokenHash\" !~ '^v1:[A-Za-z0-9+/]{{43}}=$';"))
                    .ShouldBe(0, "a retired invitation carries a readable digest, so loading it cannot throw.");
            }

            await AssertInvitationSchema(connectionString!);
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    /// <summary>
    /// A settled invitation is frozen by <c>TR_Invitations_PreventSettledChange</c>, and the canonicalization
    /// migrations rewrite exactly those rows. They also rewrite the collision losers they cancelled a statement
    /// earlier, so the trigger fires against the migration's own work. This seeds every shape at once — settled
    /// accepted, settled cancelled, an unreadable hash, a casing collision and a composition collision — and
    /// requires the upgrade to carry them across with the guard operative afterwards.
    /// </summary>
    [Test]
    public async Task Invitation_canonicalization_upgrades_settled_and_colliding_rows_and_leaves_the_trigger_operative()
    {
        var databaseName = $"invitation_settled_upgrade_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var shared = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(shared) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, shared);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var tenantId = Guid.NewGuid();
            var identityId = Guid.NewGuid();
            await using (var seedContext = new ApplicationDbContext(options))
            {
                await seedContext.Database.GetService<IMigrator>().MigrateAsync(CanonicalFormPredecessor);
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await Execute(connection, $"INSERT INTO \"Tenants\" (\"Id\", \"Type\", \"Status\", \"Slug\", \"AuthorizationVersion\") VALUES ('{tenantId}', 'Organization', 'Active', 'settled-{tenantId:N}', 0);");
                await SeedIdentity(connection, identityId);

                await SeedSettledInvitation(connection, tenantId, "v7:unreadable-legacy-digest", "accepted@example.test", "Accepted", identityId);
                await SeedSettledInvitation(connection, tenantId, $"v1:{Digest()}", "cancelled@example.test", "Cancelled", null);
                await SeedLegacyInvitation(connection, tenantId, "v9:also-unreadable", "pending@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "АННА@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "анна@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "josé@example.test");
                await SeedLegacyInvitation(connection, tenantId, $"v1:{Digest()}", "josé@example.test");
            }

            await using (var upgradedContext = new ApplicationDbContext(options))
            {
                await Should.NotThrowAsync(
                    () => upgradedContext.Database.GetService<IMigrator>().MigrateAsync(),
                    "a settled invitation and a collision loser must not abort the upgrade.");
                (await upgradedContext.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            }

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                (await Scalar<long>(connection, $"SELECT count(*) FROM \"Invitations\" WHERE \"TenantId\" = '{tenantId}';"))
                    .ShouldBe(7, "invitation history survives the upgrade, settled rows included.");
                (await Scalar<long>(connection, "SELECT count(*) FROM \"Invitations\" WHERE \"NormalizedEmail\" <> lower(normalize(\"NormalizedEmail\", NFC));"))
                    .ShouldBe(0, "every recipient reaches one canonical form.");
                (await Scalar<long>(connection, $"SELECT count(*) FROM \"Invitations\" WHERE \"TenantId\" = '{tenantId}' AND \"Status\" = 'Pending';"))
                    .ShouldBe(2, "one live offer survives per canonicalized recipient.");

                (await Scalar<bool>(connection, "SELECT tgenabled = 'O' FROM pg_trigger WHERE tgname = 'TR_Invitations_PreventSettledChange';"))
                    .ShouldBeTrue("a migration that lifts the trigger must restore it.");
                var settledChange = await Should.ThrowAsync<PostgresException>(() => Execute(
                    connection,
                    $"UPDATE \"Invitations\" SET \"ExpiresAt\" = \"ExpiresAt\" + INTERVAL '1 day' WHERE \"TenantId\" = '{tenantId}' AND \"Status\" = 'Accepted';"));
                settledChange.MessageText.ShouldContain("settled invitation");
            }
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    private static Task SeedIdentity(NpgsqlConnection connection, Guid identityId) => Execute(
        connection,
        "INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") " +
        $"VALUES ('{identityId}', 'settled@example.test', 'SETTLED@EXAMPLE.TEST', 'settled@example.test', 'SETTLED@EXAMPLE.TEST', true, 'x', 'x', 'x', false, false, true, 0);");

    /// <summary>Writes a terminal row the way a settled invitation looks once the trigger has frozen it.</summary>
    private static async Task SeedSettledInvitation(NpgsqlConnection connection, Guid tenantId, string tokenHash, string recipient, string status, Guid? acceptedBy)
    {
        var terminal = status == "Accepted" ? $"NOW(), '{acceptedBy}', NULL" : "NULL, NULL, NOW()";
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\", \"AcceptedAt\", \"AcceptedByIdentityId\", \"CancelledAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{tenantId}', @hash, @recipient, '{status}', NOW() - INTERVAL '1 day', NOW() + INTERVAL '7 days', {terminal});",
            connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("recipient", recipient);
        await command.ExecuteNonQueryAsync();
    }

    private static string Digest() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Writes the row as the previous schema would have accepted it, bypassing the aggregate entirely: the point
    /// is the data an already-deployed database is holding, not data this code could still produce.
    /// </summary>
    private static async Task SeedLegacyInvitation(NpgsqlConnection connection, Guid tenantId, string tokenHash, string recipient)
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") VALUES ('{Guid.NewGuid()}', '{tenantId}', @hash, @recipient, 'Pending', NOW(), NOW() + INTERVAL '7 days');",
            connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("recipient", recipient);
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task TenantAuthorization_concurrent_catalog_synchronization_is_atomic_and_idempotent()
    {
        var databaseName = $"tenant_authorization_catalog_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var migrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            await using (var migrationContext = new ApplicationDbContext(migrationOptions))
            {
                await migrationContext.Database.GetService<IMigrator>().MigrateAsync();
            }

            var barrier = new CatalogSynchronizationBarrierInterceptor();
            var synchronizationTasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(connectionString)
                    .AddInterceptors(barrier)
                    .Options;
                await using var context = new ApplicationDbContext(options);
                await new PermissionCatalogSynchronizer(context).SynchronizeAsync(CancellationToken.None);
            })).ToArray();

            await Task.WhenAll(synchronizationTasks);

            await using var verificationContext = new ApplicationDbContext(migrationOptions);
            var persisted = await verificationContext.Permissions.AsNoTracking().OrderBy(permission => permission.Code).ToListAsync();
            persisted.Select(permission => permission.Code).ShouldBe(Permissions.Catalog.Select(definition => definition.Code).Order(StringComparer.Ordinal).ToArray());
            foreach (var definition in Permissions.Catalog)
            {
                persisted.Single(permission => permission.Code == definition.Code).AllowedTenantTypes.SetEquals(definition.AllowedTenantTypes).ShouldBeTrue();
            }
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task Authorization_denial_audit_migration_downgrades_after_a_tenantless_denial_exists()
    {
        var databaseName = $"authorization_denial_downgrade_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var sharedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sharedConnectionString = sharedContext.Database.GetConnectionString() ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(sharedConnectionString) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, sharedConnectionString);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            await using (var context = new ApplicationDbContext(options))
            {
                await context.Database.GetService<IMigrator>().MigrateAsync();
                context.AuditEvents.Add(AuditEvent.CreateAuthorizationDenied(null, null, null, "downgrade-denial", "todos.read", "permission_denied", DateTimeOffset.UtcNow));
                await context.SaveChangesAsync();

                await context.Database.GetService<IMigrator>().MigrateAsync("20260901012806_TenantAuthorization");
            }
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    private static async Task<int> SeedBaselineData(DbContextOptions<ApplicationDbContext> options, Guid userId, Guid roleId)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.GetService<IMigrator>().MigrateAsync(BaselineMigration);
        await using var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await Execute(connection, $"INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ('{userId}', 'upgrade@example.test', 'UPGRADE@EXAMPLE.TEST', 'upgrade@example.test', 'UPGRADE@EXAMPLE.TEST', FALSE, FALSE, FALSE, FALSE, 0);");
        await Execute(connection, $"INSERT INTO \"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\") VALUES ('{roleId}', 'upgrade-role', 'UPGRADE-ROLE');");
        await Execute(connection, $"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ('{userId}', '{roleId}');");
        await Execute(connection, $"INSERT INTO \"TodoLists\" (\"Title\", \"Colour_Code\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES ('upgrade list', 'Grey', NOW(), '{userId}', NOW(), '{userId}');");

        await using var todoCommand = new NpgsqlCommand($"INSERT INTO \"TodoItems\" (\"ListId\", \"Title\", \"Priority\", \"Done\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES (1, 'upgrade sentinel', 0, FALSE, NOW(), '{userId}', NOW(), '{userId}') RETURNING \"Id\";", connection);
        return (int)(await todoCommand.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The Platform schema arrives in two ordered steps, and the order is load-bearing: an MFA enrollment binds
    /// to an invitation, so a database that had the second table without the first would hold enrollments with
    /// nothing to bind to. This walks a baseline database up through both and checks that pre-existing rows
    /// survive, that each step adds only its own tables, and that nothing is left pending at the end.
    /// </summary>
    [Test]
    public async Task Platform_schema_upgrades_in_order_and_preserves_preexisting_data()
    {
        var databaseName = $"platform_round_trip_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var shared = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()
                    ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(shared) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, shared);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var todoId = await SeedPreRegistrationMessagingData(options, userId, Guid.NewGuid());

            await using (var before = new ApplicationDbContext(options))
            {
                await before.Database.GetService<IMigrator>().MigrateAsync(PlatformInvitationPredecessor);
            }

            await AssertTableAsync(connectionString!, "PlatformAdminInvitations", false);
            await AssertTableAsync(connectionString!, "PlatformMfaEnrollments", false);

            await using (var invitations = new ApplicationDbContext(options))
            {
                await invitations.Database.GetService<IMigrator>().MigrateAsync(PlatformMfaPredecessor);
            }

            await AssertTableAsync(connectionString!, "PlatformAdminInvitations", true);
            await AssertTableAsync(connectionString!, "PlatformMfaEnrollments", false,
                "the MFA step is what adds the enrollment table, and it has not run yet.");

            await using var latest = new ApplicationDbContext(options);
            await latest.Database.GetService<IMigrator>().MigrateAsync();

            await AssertTableAsync(connectionString!, "PlatformMfaEnrollments", true);
            await AssertTableAsync(connectionString!, "PlatformRecoveryCodes", true);
            (await latest.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            (await latest.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
            (await latest.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    [Test]
    public async Task Personal_identity_and_shared_budget_schema_upgrade_in_order_and_preserve_preexisting_data()
    {
        var databaseName = $"personal_round_trip_{Guid.NewGuid():N}";
        string? connectionString = null;

        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var shared = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()
                    ?? throw new InvalidOperationException("The test PostgreSQL connection string is required.");
                connectionString = new NpgsqlConnectionStringBuilder(shared) { Database = databaseName }.ConnectionString;
                await CreateDatabase(databaseName, shared);
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            var userId = Guid.NewGuid();
            var todoId = await SeedPreRegistrationMessagingData(options, userId, Guid.NewGuid());

            await using (var before = new ApplicationDbContext(options))
            {
                await before.Database.GetService<IMigrator>().MigrateAsync(PersonalIdentityPredecessor);
            }

            await AssertTableAsync(connectionString!, "PersonProfiles", false);
            await AssertTableAsync(connectionString!, "IdentityAttemptBudgets", false);

            await using (var people = new ApplicationDbContext(options))
            {
                await people.Database.GetService<IMigrator>().MigrateAsync(SharedBudgetPredecessor);
            }

            await AssertTableAsync(connectionString!, "PersonProfiles", true);
            await AssertTableAsync(connectionString!, "PersonalTenantOwnerships", true);
            await AssertTableAsync(connectionString!, "IdentityDocuments", true);
            await AssertTableAsync(connectionString!, "IdentityDocumentFingerprints", true);
            await AssertTableAsync(connectionString!, "IdentityAttemptBudgets", false,
                "the shared budget step is what adds that table, and it has not run yet.");

            await using var latest = new ApplicationDbContext(options);
            await latest.Database.GetService<IMigrator>().MigrateAsync();

            await AssertTableAsync(connectionString!, "IdentityAttemptBudgets", true);
            await AssertTableAsync(connectionString!, "pending_personal_intents", true,
                "the personal signup's unproved phase needs its own intent table, added after the budget step.");
            await AssertTableAsync(connectionString!, "RecentIdentityProofs", true);
            await AssertTableAsync(connectionString!, "IdentitySecurityStates", true);
            (await latest.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            (await latest.Users.SingleAsync(user => user.Id == userId)).NormalizedEmail.ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
            (await latest.TodoItems.SingleAsync(todo => todo.Id == todoId)).CreatedBy.ShouldBe(userId);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // The two constraints that make the shape mean something: a document number is unique across every
            // retained key version, and a fingerprint that does not name its key version is not storable.
            (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UX_IdentityDocumentFingerprints_Fingerprint');"))
                .ShouldBeTrue();
            (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_IdentityDocumentFingerprints_Fingerprint');"))
                .ShouldBeTrue();
            (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_IdentityDocuments_Document');"))
                .ShouldBeTrue();
            (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_IdentityAttemptBudgets_Window');"))
                .ShouldBeTrue();

            // Sessions that predate the coexistence change were backfilled with a reference of their own rather
            // than the empty default the column was added with, so the unique index holds over old rows too.
            (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_UserSessions_PublicRef');"))
                .ShouldBeTrue();
            (await Scalar<long>(connection, "SELECT count(*) FROM \"UserSessions\" WHERE \"PublicRef\" = '';"))
                .ShouldBe(0);

            var violation = await Should.ThrowAsync<PostgresException>(() => Execute(connection,
                """
                INSERT INTO "IdentityDocumentFingerprints" ("IdentityId", "KeyVersion", "Fingerprint")
                VALUES ('11111111-1111-1111-1111-111111111111', 1, 'plain-text-fingerprint');
                """));
            violation.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        }
        finally
        {
            if (connectionString is not null)
            {
                await DropDatabase(databaseName, connectionString);
            }
        }
    }

    private static async Task AssertTableAsync(string connectionString, string table, bool expected, string? because = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        (await Scalar<bool>(connection, $"SELECT to_regclass('public.\"{table}\"') IS NOT NULL;"))
            .ShouldBe(expected, because ?? $"{table} presence after this step");
    }

    private static async Task<int> SeedPreRegistrationMessagingData(DbContextOptions<ApplicationDbContext> options, Guid userId, Guid roleId)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.GetService<IMigrator>().MigrateAsync(RegistrationMessagingPredecessor);
        await using var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await Execute(connection, $"INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ('{userId}', 'roundtrip@example.test', 'ROUNDTRIP@EXAMPLE.TEST', 'roundtrip@example.test', 'ROUNDTRIP@EXAMPLE.TEST', FALSE, FALSE, FALSE, FALSE, 0);");
        await Execute(connection, $"INSERT INTO \"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\") VALUES ('{roleId}', 'roundtrip-role', 'ROUNDTRIP-ROLE');");
        await Execute(connection, $"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ('{userId}', '{roleId}');");
        await Execute(connection, $"INSERT INTO \"TodoLists\" (\"Title\", \"Colour_Code\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES ('roundtrip list', 'Grey', NOW(), '{userId}', NOW(), '{userId}');");
        await using var todoCommand = new NpgsqlCommand($"INSERT INTO \"TodoItems\" (\"ListId\", \"Title\", \"Priority\", \"Done\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES (1, 'roundtrip sentinel', 0, FALSE, NOW(), '{userId}', NOW(), '{userId}') RETURNING \"Id\";", connection);
        return (int)(await todoCommand.ExecuteScalarAsync())!;
    }

    private static async Task<int> SeedPreInvitationData(DbContextOptions<ApplicationDbContext> options, Guid userId, Guid roleId)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.GetService<IMigrator>().MigrateAsync(InvitationsPredecessor);
        await using var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await Execute(connection, $"INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ('{userId}', 'roundtrip@example.test', 'ROUNDTRIP@EXAMPLE.TEST', 'roundtrip@example.test', 'ROUNDTRIP@EXAMPLE.TEST', FALSE, FALSE, FALSE, FALSE, 0);");
        await Execute(connection, $"INSERT INTO \"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\") VALUES ('{roleId}', 'roundtrip-role', 'ROUNDTRIP-ROLE');");
        await Execute(connection, $"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ('{userId}', '{roleId}');");
        await Execute(connection, $"INSERT INTO \"TodoLists\" (\"Title\", \"Colour_Code\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES ('roundtrip list', 'Grey', NOW(), '{userId}', NOW(), '{userId}');");
        await using var todoCommand = new NpgsqlCommand($"INSERT INTO \"TodoItems\" (\"ListId\", \"Title\", \"Priority\", \"Done\", \"Created\", \"CreatedBy\", \"LastModified\", \"LastModifiedBy\") VALUES (1, 'roundtrip sentinel', 0, FALSE, NOW(), '{userId}', NOW(), '{userId}') RETURNING \"Id\";", connection);
        return (int)(await todoCommand.ExecuteScalarAsync())!;
    }

    /// <summary>Leaves a real invitation and its offered role behind, so the downgrade runs against data.</summary>
    private static async Task SeedInvitation(ApplicationDbContext context)
    {
        var tenant = CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant.CreateOrganization(
            CleanArchitecture.Domain.IdentityAccess.Tenants.TenantSlug.From($"invitation-migration-{Guid.NewGuid():N}"));
        var role = CleanArchitecture.Domain.IdentityAccess.Authorization.Role.Create(tenant, "Operators");
        var invitation = CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation.Issue(
            tenant,
            $"migration-{Guid.NewGuid():N}@example.test",
            [role],
            CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash.Of(Guid.NewGuid().ToString()),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));
        // A second, accepted invitation so the down-path also runs against a row carrying the AspNetUsers key.
        var identityId = Guid.NewGuid();
        var accepted = CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation.Issue(
            tenant,
            $"migration-accepted-{Guid.NewGuid():N}@example.test",
            [role],
            CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash.Of(Guid.NewGuid().ToString()),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));
        accepted.Accept(tenant, identityId, DateTimeOffset.UtcNow.AddMinutes(1));
        context.Users.Add(new CleanArchitecture.Infrastructure.Identity.ApplicationUser
        {
            Id = identityId,
            UserName = $"migration-{identityId:N}",
            Email = $"migration-{identityId:N}@test.invalid"
        });
        context.AddRange(tenant, role, invitation, accepted);
        await context.SaveChangesAsync();
    }

    private static async Task AssertTaskSevenSchemaIsAbsent(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<bool>(connection, "SELECT to_regclass('public.outbox_messages') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.outbox_secrets') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.registration_submissions') IS NULL;")).ShouldBeTrue();

        // Downgrading this far also unwinds every later migration, so this path must keep proving that each of
        // them removed what it created.
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"UserSessions\"') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"Invitations\"') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"InvitationRoles\"') IS NULL;")).ShouldBeTrue();
    }

    private static async Task AssertInvitationSchemaIsAbsent(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"Invitations\"') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"InvitationRoles\"') IS NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"UserSessions\"') IS NOT NULL;")).ShouldBeTrue("only the invitation schema may be removed");
    }

    private static async Task AssertInvitationSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"Invitations\"') IS NOT NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"InvitationRoles\"') IS NOT NULL;")).ShouldBeTrue();
        foreach (var constraint in new[] { "CK_Invitations_Ids_NotEmpty", "CK_Invitations_Lifecycle", "CK_InvitationRoles_TenantId_NotEmpty" })
        {
            (await Scalar<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{constraint}' AND contype = 'c');")).ShouldBeTrue();
        }

        foreach (var foreignKey in new[] { "FK_Invitations_Tenants_TenantId", "FK_Invitations_AspNetUsers_AcceptedByIdentityId", "FK_InvitationRoles_Invitations_TenantId_InvitationId", "FK_InvitationRoles_Roles_TenantId_RoleId" })
        {
            (await Scalar<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{foreignKey}' AND contype = 'f');")).ShouldBeTrue();
        }

        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'AK_Invitations_TenantId_Id' AND contype = 'u');")).ShouldBeTrue();
        foreach (var index in new[] { "IX_Invitations_TokenHash", "IX_Invitations_TenantId_NormalizedEmail", "IX_Invitations_TenantId_NormalizedEmail_Status", "IX_Invitations_ExpiresAt", "IX_InvitationRoles_TenantId_RoleId" })
        {
            (await Scalar<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{index}');")).ShouldBeTrue();
        }

        // PostgreSQL normalises an index predicate, so the stored expression is never the literal that was
        // written; only its presence and uniqueness are asserted here.
        (await Scalar<bool>(connection, "SELECT indisunique AND indpred IS NOT NULL FROM pg_index WHERE indexrelid = 'public.\"IX_Invitations_TenantId_NormalizedEmail\"'::regclass;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT indisunique AND indpred IS NULL FROM pg_index WHERE indexrelid = 'public.\"IX_Invitations_TokenHash\"'::regclass;")).ShouldBeTrue();

        // The shipped DDL, not the EF model: 'r' is RESTRICT and 'a' is NO ACTION. A future accidental cascade
        // would silently erase invitation history, so the action itself is asserted (IA-REQ-036).
        foreach (var restricted in new[] { "FK_Invitations_Tenants_TenantId", "FK_InvitationRoles_Invitations_TenantId_InvitationId", "FK_InvitationRoles_Roles_TenantId_RoleId" })
        {
            (await Scalar<string>(connection, $"SELECT confdeltype::text FROM pg_constraint WHERE conname = '{restricted}';")).ShouldBe("r");
        }

        (await Scalar<string>(connection, "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'FK_Invitations_AspNetUsers_AcceptedByIdentityId';")).ShouldBe("a");
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_Invitations_PreventSettledChange' AND NOT tgisinternal);")).ShouldBeTrue();
    }

    private static async Task AssertUserSessionSchemaIsAbsent(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"UserSessions\"') IS NULL;")).ShouldBeTrue();
    }

    private static async Task AssertUserSessionSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<bool>(connection, "SELECT to_regclass('public.\"UserSessions\"') IS NOT NULL;")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_UserSessions_Lifecycle' AND contype = 'c');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_UserSessions_AspNetUsers_IdentityId' AND contype = 'f');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_UserSessions_Tenants_ActiveTenantId' AND contype = 'f');")).ShouldBeTrue();
        foreach (var index in new[] { "IX_UserSessions_IdentityId", "IX_UserSessions_IdentityId_RevokedAt_AbsoluteExpiresAt" })
        {
            (await Scalar<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{index}');")).ShouldBeTrue();
        }
        await AssertUserSessionChronologyConstraint(connection);
    }

    private static async Task AssertUserSessionChronologyConstraint(NpgsqlConnection connection)
    {
        var userId = Guid.NewGuid();
        await Execute(connection, $"INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ('{userId}', 'session-chronology@example.test', 'SESSION-CHRONOLOGY@EXAMPLE.TEST', 'session-chronology@example.test', 'SESSION-CHRONOLOGY@EXAMPLE.TEST', FALSE, FALSE, FALSE, FALSE, 0);");
        // Truncated to whole microseconds, which is all PostgreSQL stores. The clock supplies a further
        // hundred-nanosecond digit that the server has to round away, and when that digit is a tie the
        // rounding can land both timestamps on the same microsecond — erasing the one-microsecond gap this
        // is about and letting a row the constraint forbids through perhaps one run in twenty.
        var clock = DateTimeOffset.UtcNow;
        var createdAt = clock.AddTicks(-(clock.Ticks % 10));
        var invalid = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"INSERT INTO \"UserSessions\" (\"Id\", \"IdentityId\", \"CreatedAt\", \"LastSeenAt\", \"IdleExpiresAt\", \"AbsoluteExpiresAt\", \"RevokedAt\", \"Version\") VALUES ('{Guid.NewGuid()}', '{userId}', '{createdAt:O}', '{createdAt:O}', '{createdAt.AddMinutes(1):O}', '{createdAt.AddHours(1):O}', '{createdAt.AddMicroseconds(-1):O}', 1);"));
        invalid.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private static async Task AssertPreexistingSentinelsAndAuditTrigger(string connectionString, Guid userId, int todoId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<string>(connection, $"SELECT \"NormalizedEmail\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}';")).ShouldBe("ROUNDTRIP@EXAMPLE.TEST");
        (await Scalar<string>(connection, $"SELECT \"CreatedBy\"::text FROM \"TodoItems\" WHERE \"Id\" = {todoId};")).ShouldBe(userId.ToString());
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_AuditEvents_AppendOnly' AND NOT tgisinternal);")).ShouldBeTrue();
        await AssertAuditTriggerRejectsMutation(connectionString);
    }

    private static async Task AssertAuditTriggerRejectsMutation(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var auditEventId = await Scalar<Guid>(connection, "INSERT INTO \"AuditEvents\" (\"Id\", \"EventType\", \"OccurredAt\", \"CorrelationId\", \"Metadata\") VALUES (gen_random_uuid(), 'roundtrip.audit.sentinel', NOW(), 'roundtrip-audit-sentinel', '{}'::jsonb) RETURNING \"Id\";");
        var mutation = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"UPDATE \"AuditEvents\" SET \"EventType\" = 'mutated' WHERE \"Id\" = '{auditEventId}';"));
        mutation.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
    }

    private static async Task AssertSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = 'Id';")).ShouldBe("uuid");
        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'TodoItems' AND column_name = 'CreatedBy';")).ShouldBe("uuid");
        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'Roles' AND column_name = 'IsRetired';")).ShouldBe("boolean");
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AspNetUsers_Id_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AspNetRoles_Id_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AuditEvents_Ids_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_TodoItems_AuditActors_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TenantMemberships_AspNetUsers_IdentityId');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_MembershipRoles_TenantMemberships_TenantId_MembershipId');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_MembershipRoles_Roles_TenantId_RoleId');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_RolePermissions_Roles_TenantId_RoleId');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_RolePermissions_Permissions_PermissionCode');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_outbox_messages_Id_NotEmpty' AND contype = 'c');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_registration_submissions_Id_NotEmpty' AND contype = 'c');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_outbox_secrets_Ids_NotEmpty' AND contype = 'c');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_outbox_secrets_Lifecycle' AND contype = 'c');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_outbox_secrets_outbox_messages_OutboxMessageId' AND contype = 'f');")).ShouldBeTrue();
        foreach (var index in new[] { "IX_outbox_messages_NextAttemptAt", "IX_outbox_secrets_OutboxMessageId", "IX_outbox_secrets_VersionedHash", "IX_registration_submissions_CanonicalKey" })
        {
            (await Scalar<bool>(connection, $"SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{index}');")).ShouldBeTrue();
        }
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'EmailIndex' AND indexdef LIKE 'CREATE UNIQUE INDEX%');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_AuditEvents_AppendOnly' AND NOT tgisinternal);")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_Roles_PreventSystemDeletion' AND NOT tgisinternal);")).ShouldBeTrue();
        await AssertInvitationSchema(connectionString);

        var zeroIdException = await Should.ThrowAsync<PostgresException>(() => Execute(connection, "INSERT INTO \"AspNetRoles\" (\"Id\") VALUES ('00000000-0000-0000-0000-000000000000');"));
        zeroIdException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);

        var messageId = Guid.NewGuid();
        await Execute(connection, $"INSERT INTO outbox_messages (\"Id\", \"Type\", \"Payload\", \"AttemptCount\", \"NextAttemptAt\", \"CreatedAt\") VALUES ('{messageId}', 'identity.confirmation.requested', '{{}}'::jsonb, 0, NOW(), NOW());");
        var invalidDeliveredSecret = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"INSERT INTO outbox_secrets (\"Id\", \"OutboxMessageId\", \"VersionedHash\", \"Ciphertext\", \"ExpiresAt\", \"Status\") VALUES ('{Guid.NewGuid()}', '{messageId}', 'v1:invalid', 'must-not-remain', NOW() + INTERVAL '1 hour', 'Delivered');"));
        invalidDeliveredSecret.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);

        var consumedMessageId = Guid.NewGuid();
        await Execute(connection, $"INSERT INTO outbox_messages (\"Id\", \"Type\", \"Payload\", \"AttemptCount\", \"NextAttemptAt\", \"CreatedAt\") VALUES ('{consumedMessageId}', 'identity.confirmation.requested', '{{}}'::jsonb, 0, NOW(), NOW());");
        var consumedWithoutReceipt = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"INSERT INTO outbox_secrets (\"Id\", \"OutboxMessageId\", \"VersionedHash\", \"ExpiresAt\", \"Status\", \"DeliveryReason\", \"DeliveredAt\", \"TerminalReason\", \"CompletedAt\") VALUES ('{Guid.NewGuid()}', '{consumedMessageId}', 'v1:partial-consumed', NOW() + INTERVAL '1 hour', 'Consumed', 'provider_delivered', NOW(), 'confirmation_consumed', NOW());"));
        consumedWithoutReceipt.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);

        var failedMessageId = Guid.NewGuid();
        await Execute(connection, $"INSERT INTO outbox_messages (\"Id\", \"Type\", \"Payload\", \"AttemptCount\", \"NextAttemptAt\", \"CreatedAt\") VALUES ('{failedMessageId}', 'identity.confirmation.requested', '{{}}'::jsonb, 0, NOW(), NOW());");
        var failedWithReceiptOnly = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"INSERT INTO outbox_secrets (\"Id\", \"OutboxMessageId\", \"VersionedHash\", \"ExpiresAt\", \"Status\", \"ProviderReceipt\", \"TerminalReason\", \"CompletedAt\") VALUES ('{Guid.NewGuid()}', '{failedMessageId}', 'v1:partial-failed', NOW() + INTERVAL '1 hour', 'Failed', 'provider-receipt', 'delivery_failed', NOW());"));
        failedWithReceiptOnly.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private static async Task AssertDowngrade(DbContextOptions<ApplicationDbContext> options, Guid userId, int todoId)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.GetService<IMigrator>().MigrateAsync(BaselineMigration);
        await using var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = 'Id';")).ShouldBe("text");
        (await Scalar<string>(connection, $"SELECT \"CreatedBy\" FROM \"TodoItems\" WHERE \"Id\" = {todoId};")).ShouldBe(userId.ToString());
    }

    private static async Task CreateDatabase(string databaseName, string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"CREATE DATABASE \"{databaseName}\";");
    }

    private static async Task DropDatabase(string databaseName, string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class CatalogSynchronizationBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothSynchronizersStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _observedSynchronizationCommands;
        private int _synchronizationMode;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                Interlocked.CompareExchange(ref _synchronizationMode, 1, 0);
                await WaitForBothSynchronizersAsync(cancellationToken);
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _synchronizationMode) != 1 && command.CommandText.Contains("FROM \"Permissions\"", StringComparison.Ordinal))
            {
                Interlocked.CompareExchange(ref _synchronizationMode, 2, 0);
                await WaitForBothSynchronizersAsync(cancellationToken);
            }

            return result;
        }

        private async Task WaitForBothSynchronizersAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _observedSynchronizationCommands) == 2)
            {
                _bothSynchronizersStarted.TrySetResult();
            }

            await _bothSynchronizersStarted.Task.WaitAsync(cancellationToken);
        }
    }
}
