using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class MigrationUpgradeTests
{
    private const string BaselineMigration = "20260831183429_BaselinePostgreSql";

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

    private static async Task AssertSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = 'Id';")).ShouldBe("uuid");
        (await Scalar<string>(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'TodoItems' AND column_name = 'CreatedBy';")).ShouldBe("uuid");
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AspNetUsers_Id_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AspNetRoles_Id_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_AuditEvents_Ids_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_TodoItems_AuditActors_NotEmpty');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TenantMemberships_AspNetUsers_IdentityId');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'EmailIndex' AND indexdef LIKE 'CREATE UNIQUE INDEX%');")).ShouldBeTrue();
        (await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_AuditEvents_AppendOnly' AND NOT tgisinternal);")).ShouldBeTrue();

        var zeroIdException = await Should.ThrowAsync<PostgresException>(() => Execute(connection, "INSERT INTO \"AspNetRoles\" (\"Id\") VALUES ('00000000-0000-0000-0000-000000000000');"));
        zeroIdException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
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
}
