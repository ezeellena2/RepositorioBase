using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class SessionFoundationContractTests
{
    [Test]
    public void Current_session_shell_fails_closed_without_persisted_validation()
    {
        var session = new CurrentSession(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        session.IsInvalid.ShouldBeTrue();
        session.IdentityId.ShouldBeNull();
        session.SessionId.ShouldBeNull();
    }

    [Test]
    public void Session_routes_expose_exact_runtime_verb_and_authorization_metadata()
    {
        using var scope = TestServices.CreateScope();
        var endpoints = scope.ServiceProvider.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

        AssertRoute(endpoints, "/api/identity/sessions", "POST", false);
        AssertRoute(endpoints, "/api/identity/sessions/current", "DELETE", true);
        AssertRoute(endpoints, "/api/identity/context", "GET", true);
        AssertRoute(endpoints, "/api/identity/context/tenant", "PUT", true);
    }

    [Test]
    public void Session_mapping_requires_restrict_relationships_concurrency_and_lifecycle_check()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserSession))!;

        session.FindProperty(nameof(UserSession.Version))!.IsConcurrencyToken.ShouldBeTrue();
        session.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(UserSession.IdentityId)).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        session.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(UserSession.ActiveTenantId)).DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        session.GetIndexes().ShouldContain(index => index.Properties.Single().Name == nameof(UserSession.IdentityId));
        session.GetCheckConstraints().ShouldContain(check => check.Name == "CK_UserSessions_Lifecycle" && check.Sql.Contains("RevokedAt", StringComparison.Ordinal));
    }

    [Test]
    public async Task PostgreSql_persists_sessions_enforces_concurrency_restrict_foreign_keys_and_lifecycle_branches()
    {
        string connectionString = string.Empty;
        string adminConnectionString = string.Empty;
        string databaseName = $"session_contract_{Guid.NewGuid():N}";
        var created = false;
        try
        {
            using (var scope = TestServices.CreateScope())
            {
                var template = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
                connectionString = new NpgsqlConnectionStringBuilder(template) { Database = databaseName, Pooling = false }.ConnectionString;
                adminConnectionString = new NpgsqlConnectionStringBuilder(template) { Database = "postgres", Pooling = false }.ConnectionString;
            }

            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await Execute(admin, $"CREATE DATABASE \"{databaseName}\";");
                created = true;
            }

            await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            await context.Database.MigrateAsync();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"session-{Guid.NewGuid():N}", NormalizedUserName = $"SESSION-{Guid.NewGuid():N}", Email = $"session-{Guid.NewGuid():N}@example.test", NormalizedEmail = $"SESSION-{Guid.NewGuid():N}@EXAMPLE.TEST" };
            var tenant = Tenant.CreateOrganization(TenantSlug.From($"session-{Guid.NewGuid():N}"));
            var now = DateTimeOffset.UtcNow;
            var idleExpiresAt = now.AddMinutes(30);
            var absoluteExpiresAt = now.AddHours(12);
            var session = UserSession.Create(user.Id, now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
            session.SelectTenant(tenant.Id, now);
            context.AddRange(user, tenant, session);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            var reloaded = await context.UserSessions.SingleAsync(item => item.Id == session.Id);
            reloaded.Id.ShouldBe(session.Id);
            reloaded.IdentityId.ShouldBe(user.Id);
            reloaded.ActiveTenantId.ShouldBe(tenant.Id);
            reloaded.CreatedAt.ToUnixTimeMilliseconds().ShouldBe(now.ToUnixTimeMilliseconds());
            reloaded.LastSeenAt.ToUnixTimeMilliseconds().ShouldBe(now.ToUnixTimeMilliseconds());
            reloaded.IdleExpiresAt.ToUnixTimeMilliseconds().ShouldBe(idleExpiresAt.ToUnixTimeMilliseconds());
            reloaded.AbsoluteExpiresAt.ToUnixTimeMilliseconds().ShouldBe(absoluteExpiresAt.ToUnixTimeMilliseconds());
            reloaded.RevokedAt.ShouldBeNull();
            reloaded.Version.ShouldBe(2);

            await using var first = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            await using var second = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            var firstSession = await first.UserSessions.SingleAsync(item => item.Id == session.Id);
            var secondSession = await second.UserSessions.SingleAsync(item => item.Id == session.Id);
            // Activity tracking never conflicts: a touch persisted from one context does not invalidate another.
            firstSession.Touch(now.AddMinutes(1));
            await first.SaveChangesAsync();
            secondSession.Touch(now.AddMinutes(2));
            await second.SaveChangesAsync();

            await using var sql = new NpgsqlConnection(connectionString);
            await sql.OpenAsync();
            (await Scalar<bool>(sql, "SELECT confdeltype = 'r' FROM pg_constraint WHERE conname = 'FK_UserSessions_AspNetUsers_IdentityId';")).ShouldBeTrue();
            (await Scalar<bool>(sql, "SELECT confdeltype = 'r' FROM pg_constraint WHERE conname = 'FK_UserSessions_Tenants_ActiveTenantId';")).ShouldBeTrue();
            await AssertRestrictDelete(connectionString, $"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{user.Id}';");
            await AssertRestrictDelete(connectionString, $"DELETE FROM \"Tenants\" WHERE \"Id\" = '{tenant.Id.Value}';");

            // State transitions are compare-and-swap: a stale revocation loses against a committed state change.
            firstSession.ClearActiveTenant(now.AddMinutes(3));
            await first.SaveChangesAsync();
            secondSession.Revoke(now.AddMinutes(4));
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            (await context.UserSessions.AsNoTracking().SingleAsync(item => item.Id == session.Id)).Version.ShouldBe(3);

            await AssertIndex(sql, "IX_UserSessions_ActiveTenantId", ["ActiveTenantId"]);
            await AssertIndex(sql, "IX_UserSessions_IdentityId", ["IdentityId"]);
            await AssertIndex(sql, "IX_UserSessions_IdentityId_RevokedAt_AbsoluteExpiresAt", ["IdentityId", "RevokedAt", "AbsoluteExpiresAt"]);
            await AssertCheck(sql, user.Id, now, now, now.AddMinutes(1), now.AddHours(1), null, 1, Guid.Empty);
            await AssertCheck(sql, Guid.Empty, now, now, now.AddMinutes(1), now.AddHours(1), null, 1);
            await AssertCheck(sql, user.Id, now, now.AddMilliseconds(-1), now.AddMinutes(1), now.AddHours(1), null, 1);
            await AssertCheck(sql, user.Id, now, now, now.AddMilliseconds(-1), now.AddHours(1), null, 1);
            await AssertCheck(sql, user.Id, now, now, now.AddMinutes(1), now.AddMilliseconds(-1), null, 1);
            await AssertCheck(sql, user.Id, now, now, now.AddMinutes(1), now.AddHours(1), now.AddMilliseconds(-1), 1);
            await AssertCheck(sql, user.Id, now, now, now.AddMinutes(1), now.AddHours(1), null, 0);
        }
        finally
        {
            if (created)
            {
                await using var admin = new NpgsqlConnection(adminConnectionString);
                await admin.OpenAsync();
                await Execute(admin, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
            }
        }
    }

    private static async Task AssertCheck(NpgsqlConnection connection, Guid userId, DateTimeOffset created, DateTimeOffset lastSeen, DateTimeOffset idle, DateTimeOffset absolute, DateTimeOffset? revoked, int version, Guid? sessionId = null)
    {
        var revokedSql = revoked is null ? "NULL" : $"'{revoked:O}'";
        var id = sessionId ?? Guid.NewGuid();
        var exception = await Should.ThrowAsync<PostgresException>(() => Execute(connection, $"INSERT INTO \"UserSessions\" (\"Id\",\"IdentityId\",\"CreatedAt\",\"LastSeenAt\",\"IdleExpiresAt\",\"AbsoluteExpiresAt\",\"RevokedAt\",\"Version\") VALUES ('{id}', '{userId}', '{created:O}', '{lastSeen:O}', '{idle:O}', '{absolute:O}', {revokedSql}, {version});"));
        exception.SqlState.ShouldBe("23514");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertRestrictDelete(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var exception = await Should.ThrowAsync<PostgresException>(() => Execute(connection, sql));
        // PostgreSQL reports an ON DELETE RESTRICT violation as 23001, distinct from NO ACTION's 23503.
        exception.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        await transaction.RollbackAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertIndex(NpgsqlConnection connection, string indexName, string[] columns)
    {
        const string sql = """
            SELECT table_class.relname,
                   array_agg(attribute.attname ORDER BY key_column.ordinality),
                   index_data.indisunique,
                   pg_get_expr(index_data.indpred, index_data.indrelid)
            FROM pg_index AS index_data
            INNER JOIN pg_class AS table_class ON table_class.oid = index_data.indrelid
            INNER JOIN pg_class AS index_class ON index_class.oid = index_data.indexrelid
            INNER JOIN pg_namespace AS table_schema ON table_schema.oid = table_class.relnamespace
            INNER JOIN LATERAL unnest(index_data.indkey) WITH ORDINALITY AS key_column(attnum, ordinality) ON TRUE
            INNER JOIN pg_attribute AS attribute ON attribute.attrelid = table_class.oid AND attribute.attnum = key_column.attnum
            WHERE table_schema.nspname = current_schema()
              AND table_class.relname = 'UserSessions'
              AND index_class.relname = @indexName
            GROUP BY table_class.relname, index_data.indisunique, index_data.indpred, index_data.indrelid;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("indexName", indexName);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue($"Index {indexName} must exist on UserSessions.");
        reader.GetString(0).ShouldBe("UserSessions");
        reader.GetFieldValue<string[]>(1).ShouldBe(columns);
        reader.GetBoolean(2).ShouldBeFalse();
        reader.IsDBNull(3).ShouldBeTrue();
    }

    private static void AssertRoute(IEnumerable<RouteEndpoint> endpoints, string route, string verb, bool requiresAuthorization)
    {
        var endpoint = endpoints.Single(candidate => candidate.RoutePattern.RawText == route && candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(verb));
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            requiresAuthorization.ShouldBeFalse(route);
            return;
        }

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any().ShouldBe(requiresAuthorization, route);
    }
}
