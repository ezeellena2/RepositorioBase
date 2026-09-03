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

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class MigrationUpgradeTests
{
    private const string BaselineMigration = "20260831183429_BaselinePostgreSql";
    private const string RegistrationMessagingPredecessor = "20260901024500_AuthorizationDenialAudit";
    private const string UserSessionsPredecessor = "20260901050000_RegistrationMessaging";
    private const string InvitationsPredecessor = "20260901184054_UserSessions";

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
            $"v1:{Convert.ToBase64String(Guid.NewGuid().ToByteArray())}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));
        // A second, accepted invitation so the down-path also runs against a row carrying the AspNetUsers key.
        var identityId = Guid.NewGuid();
        var accepted = CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation.Issue(
            tenant,
            $"migration-accepted-{Guid.NewGuid():N}@example.test",
            [role],
            $"v1:{Convert.ToBase64String(Guid.NewGuid().ToByteArray())}",
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
        var createdAt = DateTimeOffset.UtcNow;
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
