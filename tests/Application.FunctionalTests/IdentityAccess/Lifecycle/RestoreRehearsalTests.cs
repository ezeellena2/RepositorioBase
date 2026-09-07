using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Lifecycle;

/// <summary>
/// The restore rehearsal the plan asks for, against a backup that genuinely predates the writes it is supposed to
/// predate (IA-REQ-055).
/// <para>
/// **This one really restores something.** `CREATE DATABASE … TEMPLATE …` is a physical copy: PostgreSQL copies
/// the template's files block for block, so the copy holds exactly the bytes the source held at that instant and
/// nothing written afterwards. The session, the password and the sealed token this test reads back out of the
/// copy are therefore the ones from before the revocation and the replacement, not a fixture arranged to look
/// like them. Task 26 previously ticked this box with no backup taken and none restored; that is what this file
/// exists to correct.
/// </para>
/// <para>
/// **Isolation.** Every database here is created for the test and dropped after it. The persistent acceptance
/// database is never touched, never reset and never connected to — only the same PostgreSQL server is shared,
/// which is what makes `TEMPLATE` possible at all.
/// </para>
/// <para>
/// **What it still does not prove.** No external authority issued the evidence — the test signs it with a key it
/// made up — so this is a rehearsal of the adapter and the pipeline, not restore certification. And
/// "insufficient external deletion evidence keeps Personal data quarantined" is not exercised, because nothing in
/// this system yet models external deletion evidence; that clause is recorded as open rather than claimed.
/// </para>
/// </summary>
[NonParallelizable]
public sealed class RestoreRehearsalTests
{
    private const string Password = "Testing1234!";
    private const string Replaced = "Replaced5678!";
    private const string DeploymentName = "identity-access-rehearsal";
    private static readonly string Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("a key the backup does not contain"));

    private readonly List<string> _databases = [];

    [TearDown]
    public async Task Drop_every_database_this_test_created()
    {
        foreach (var database in _databases)
        {
            ReleaseConnectionsTo(database);
            await DropAsync(database);
        }

        _databases.Clear();
    }

    [Test]
    public async Task A_restored_deployment_honours_nothing_from_before_the_backup_until_an_operator_releases_it()
    {
        var origin = await CreatedDatabaseAsync();
        var email = $"rehearsal-{Guid.NewGuid():N}@example.test";

        // ---- before the backup: an ordinary deployment, doing ordinary things ----
        Guid identityId;
        string cookie;
        await using (var live = Host(origin))
        {
            identityId = await SeedConfirmedUserAsync(live, email);
            cookie = await SignInAsync(live, email, Password);
            (await RecoverPasswordAsync(live, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var beforeBackup = await SnapshotAsync(origin, identityId);
        beforeBackup.LiveSessions.ShouldBe(1, "the premise is a session that was live when the backup was taken");
        beforeBackup.PendingSecrets.ShouldBe(1, "and a token that was sealed and undelivered");

        // ---- the backup: a physical copy, taken here and not simulated ----
        var restored = await CopiedDatabaseAsync(origin);

        // ---- after the backup: the writes the copy must not contain ----
        await using (var live = Host(origin))
        {
            await RevokeEverySessionAsync(live, identityId);
            await ReplacePasswordAsync(live, identityId);
        }

        var afterOnOrigin = await SnapshotAsync(origin, identityId);
        afterOnOrigin.LiveSessions.ShouldBe(0);
        var afterOnRestored = await SnapshotAsync(restored, identityId);
        afterOnRestored.LiveSessions.ShouldBe(1, "the copy predates the revocation, which is what makes it a backup");
        afterOnRestored.PasswordHash.ShouldBe(beforeBackup.PasswordHash, "and predates the replacement");
        afterOnRestored.PasswordHash.ShouldNotBe(afterOnOrigin.PasswordHash);

        // ---- the restored deployment, armed and holding no evidence: closed ----
        await using (var closed = Host(restored, Armed(evidence: null)))
        {
            foreach (var route in new[] { "/api/identity/antiforgery", "/api/identity/context" })
            {
                using var response = await closed.Client.GetAsync($"{closed.Host}{route}");
                response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, route);
                (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString()
                    .ShouldBe("recovery_admission_closed", route);
            }

            (await DispatchAsync(closed)).ShouldBe(0, "a closed deployment dispatches no outbox delivery");
        }

        (await SnapshotAsync(restored, identityId)).PendingSecrets
            .ShouldBe(1, "and it claimed nothing, so the envelope is exactly where the backup left it");

        // ---- quarantined: authentication and revalidation, and nothing else ----
        var epoch = DateTimeOffset.UtcNow;
        await using (var quarantined = Host(restored, Armed(Signed(release: false, epoch))))
        {
            using var antiforgery = await quarantined.Client.GetAsync($"{quarantined.Host}/api/identity/antiforgery");
            antiforgery.StatusCode.ShouldBe(HttpStatusCode.OK, "signing in is what quarantine is for");

            using var context = await quarantined.Client.GetAsync($"{quarantined.Host}/api/identity/context");
            context.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, "and reading the restored data is not");

            // The cookie the backup contained. It named a session that is live in this copy, and it proves
            // nothing here: the session predates the recovery epoch, so it counts as revoked.
            using var revalidated = await PostAsync(
                quarantined,
                "/api/identity/credentials/reauthenticate",
                new { action = CleanArchitecture.Application.IdentityAccess.Credentials.ProofActions.PasswordChange, password = Password },
                session: cookie);
            revalidated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "a session from before the restore is not one this deployment issued");

            // And the password from before the replacement still opens a new session, because the copy predates
            // that write too — which is exactly why the deployment is quarantined rather than open.
            (await SignInAsync(quarantined, email, Password)).ShouldNotBeNullOrWhiteSpace();

            (await DispatchAsync(quarantined)).ShouldBe(0, "quarantine is authentication and revalidation, not delivery");
        }

        // ---- released: ordinary service, and the pre-backup token still refused ----
        await using (var open = Host(restored, Armed(Signed(release: true, epoch))))
        {
            using var response = await open.Client.GetAsync($"{open.Host}/api/identity/antiforgery");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, "a released deployment serves what it always did");

            (await DispatchAsync(open)).ShouldBe(0, "a token sealed before the restore is never sent");
        }

        var settled = await SnapshotAsync(restored, identityId);
        settled.PendingSecrets.ShouldBe(0, "the envelope from before the restore is terminalized rather than left pending");
        settled.FailedSecrets.ShouldBe(1);
    }

    /// <summary>
    /// The evidence has to come from outside the restored data, and this is the test that says so out loud: the
    /// copy contains everything the original did, and none of it opens the deployment.
    /// </summary>
    [Test]
    public async Task Nothing_inside_the_restored_database_can_open_it()
    {
        var origin = await CreatedDatabaseAsync();
        await using (var live = Host(origin)) await SeedConfirmedUserAsync(live, $"nothing-{Guid.NewGuid():N}@example.test");
        var restored = await CopiedDatabaseAsync(origin);

        // Armed, with the deployment name the copy itself would tell you, and no evidence.
        await using var closed = Host(restored, Armed(evidence: null));

        using var response = await closed.Client.GetAsync($"{closed.Host}/api/identity/antiforgery");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString()
            .ShouldBe("recovery_admission_closed");
    }

    /// <summary>A copy nobody armed is an ordinary deployment. Restoring is not by itself a reason to refuse.</summary>
    [Test]
    public async Task A_copy_of_a_deployment_nobody_armed_serves_normally()
    {
        var origin = await CreatedDatabaseAsync();
        await using (var live = Host(origin)) await SeedConfirmedUserAsync(live, $"unarmed-{Guid.NewGuid():N}@example.test");
        var restored = await CopiedDatabaseAsync(origin);

        await using var copy = Host(restored);

        (await copy.Client.GetAsync($"{copy.Host}/api/identity/antiforgery")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- the database, created and copied for this test alone ----------

    /// <summary>
    /// A database of this test's own, on the same server. The persistent acceptance database is never named,
    /// never connected to and never reset — sharing a server is what makes `TEMPLATE` possible, and is all that
    /// is shared.
    /// </summary>
    private async Task<string> CreatedDatabaseAsync()
    {
        var name = $"ia_rehearsal_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{name}\";");
        _databases.Add(name);

        // Migrated explicitly: the functional factory strips hosted services, so the migration one that a real
        // deployment starts with does not run here.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionTo(name)).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        return name;
    }

    /// <summary>
    /// The backup. `TEMPLATE` copies the source's files, so what comes out holds exactly what the source held at
    /// this instant — a real earlier copy, not a fixture arranged to resemble one.
    /// </summary>
    private async Task<string> CopiedDatabaseAsync(string source)
    {
        var name = $"{source}_restored";
        ReleaseConnectionsTo(source);

        // PostgreSQL refuses to copy a database anything is connected to.
        await ExecuteOnServerAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{source}' AND pid <> pg_backend_pid();");
        await ExecuteOnServerAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{source}\";");
        _databases.Add(name);
        return name;
    }

    /// <summary>
    /// Dropping is retried once. `DROP DATABASE` waits for every backend to go, and under a full suite a pooled
    /// connection can be handed back between the terminate and the drop — a race with the connection pool rather
    /// than a failure of anything under test, and leaving the database behind would be worse than trying again.
    /// </summary>
    private static async Task DropAsync(string database)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await ExecuteOnServerAsync(
                    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{database}' AND pid <> pg_backend_pid();");
                await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);");
                return;
            }
            catch (Exception failure) when (failure is NpgsqlException or TimeoutException && attempt == 0)
            {
                ReleaseConnectionsTo(database);
            }
        }
    }

    /// <summary>
    /// Returns this test's own pooled connections, and only its own. Clearing every pool in the process would
    /// also close the ones the rest of the suite is using against the shared database — which is somebody else's
    /// connection to drop, not this test's.
    /// </summary>
    private static void ReleaseConnectionsTo(string database)
    {
        using var pooled = new NpgsqlConnection(ConnectionTo(database));
        NpgsqlConnection.ClearPool(pooled);
    }

    /// <summary>
    /// A connection to the server rather than to any of its databases, unpooled and with a timeout of its own:
    /// these statements wait on other backends, and the default is a wait meant for queries.
    /// </summary>
    private static async Task ExecuteOnServerAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(FunctionalTestSetup.ConnectionString)
        {
            Database = "postgres",
            CommandTimeout = 120,
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionTo(string database) =>
        new NpgsqlConnectionStringBuilder(FunctionalTestSetup.ConnectionString) { Database = database }.ConnectionString;

    // ---------- the deployment under test ----------

    private static Deployment Host(string database, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = new WebApiFactory(
            ConnectionTo(database),
            Microsoft.Extensions.Hosting.Environments.Production,
            useTestAuthentication: false,
            useTestIdentityAccessDoubles: false,
            settings: settings);
        return new Deployment(factory);
    }

    private sealed class Deployment(WebApiFactory factory) : IAsyncDisposable
    {
        internal WebApiFactory Factory { get; } = factory;

        internal HttpClient Client { get; } = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        internal string Host { get; } = $"https://rehearsal-{Guid.NewGuid():N}.localhost";

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>One dispatch pass against this deployment, using its own composed services.</summary>
    private static async Task<int> DispatchAsync(Deployment deployment)
    {
        using var scope = deployment.Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchDueAsync(CancellationToken.None);
    }

    // ---------- what a deployment did before the backup ----------

    private static async Task<Guid> SeedConfirmedUserAsync(Deployment deployment, string email)
    {
        using var scope = deployment.Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Status = CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus.Active
        };
        (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
        return user.Id;
    }

    private static async Task<string> SignInAsync(Deployment deployment, string email, string password)
    {
        using var response = await PostAsync(deployment, "/api/identity/sessions", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static Task<HttpResponseMessage> RecoverPasswordAsync(Deployment deployment, string email) =>
        PostAsync(deployment, "/api/identity/credentials/password/recovery", new { email });

    /// <summary>
    /// A POST carrying its own antiforgery pair. This client keeps no cookie jar, so the pair travels explicitly
    /// — which is also what lets a test present a cookie a different deployment issued.
    /// </summary>
    private static async Task<HttpResponseMessage> PostAsync(Deployment deployment, string path, object body, string? session = null)
    {
        var antiforgery = await AntiforgeryAsync(deployment);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{deployment.Host}{path}", body, antiforgery.Token);
        request.Headers.Add("Cookie", session is null ? antiforgery.Cookie : $"{session}; {antiforgery.Cookie}");
        return await deployment.Client.SendAsync(request);
    }

    private static async Task<(string Cookie, string Token)> AntiforgeryAsync(Deployment deployment)
    {
        using var response = await deployment.Client.GetAsync($"{deployment.Host}/api/identity/antiforgery");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0];
        return (pair, token);
    }

    private static async Task RevokeEverySessionAsync(Deployment deployment, Guid identityId)
    {
        using var scope = deployment.Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.UserSessions.Where(session => session.IdentityId == identityId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow));
    }

    private static async Task ReplacePasswordAsync(Deployment deployment, Guid identityId)
    {
        using var scope = deployment.Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var credentials = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.Credentials.IIdentityCredentialService>();
        (await credentials.ReplacePasswordAsync(identityId, Replaced, CancellationToken.None)).Succeeded.ShouldBeTrue();
    }

    /// <summary>What a database holds, read directly so the comparison is of rows and not of behaviour.</summary>
    private static async Task<Snapshot> SnapshotAsync(string database, Guid identityId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionTo(database)).Options;
        await using var context = new ApplicationDbContext(options);
        return new Snapshot(
            await context.UserSessions.CountAsync(session => session.IdentityId == identityId && session.RevokedAt == null),
            await context.Users.Where(user => user.Id == identityId).Select(user => user.PasswordHash).SingleAsync(),
            await context.OutboxSecrets.CountAsync(secret => secret.Status == OutboxSecretStatus.Pending),
            await context.OutboxSecrets.CountAsync(secret => secret.Status == OutboxSecretStatus.Failed));
    }

    private sealed record Snapshot(int LiveSessions, string? PasswordHash, int PendingSecrets, int FailedSecrets);

    // ---------- the operator's evidence, held outside the database ----------

    private static Dictionary<string, string?> Armed(string? evidence) => new()
    {
        ["IdentityAccess:Recovery:Deployment"] = DeploymentName,
        ["IdentityAccess:Recovery:VerificationKey"] = Key,
        ["IdentityAccess:Recovery:Evidence"] = evidence,

        // A deliverable configuration on purpose. A dispatcher that refused because the mail was misconfigured
        // would prove nothing about admission — the refusals this test asserts have to be admission's.
        ["IdentityAccess:Email:Enabled"] = "true",
        ["IdentityAccess:Email:ApiKey"] = "rehearsal-key",
        ["IdentityAccess:Email:FromAddress"] = "no-reply@example.test"
    };

    private static string Signed(bool release, DateTimeOffset epoch)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new RecoveryEvidence(
            DeploymentName, epoch.ToString("O"), "backup-rehearsal", now.AddMinutes(-5).ToString("O"), now.AddHours(2).ToString("O"), release, null);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(
            Convert.FromBase64String(Key), Encoding.UTF8.GetBytes(ConfiguredRecoveryAdmission.Canonical(evidence))));
        return JsonSerializer.Serialize(evidence with { Signature = signature });
    }
}
