using Aspire.Hosting;

namespace CleanArchitecture.Web.AcceptanceTests;

[SetUpFixture]
public class AspireSetup
{
    /// <summary>
    /// How long the whole distributed application has to become healthy. It covers a container start, a
    /// migration, a permission-catalogue synchronization and a Vite dev server, and the budget is spent while
    /// the rest of the solution's suites are competing for the same machine — it is a ceiling rather than a wait, so a fast start costs none of it and this
    /// is set generously enough that contention cannot make a working application look like a broken one.
    /// </summary>
    internal const string PlatformBootstrapOwnerEmail = "platform-owner@example.test";

    /// <summary>
    /// Where the run's delivered mail is written. The journeys follow the links a recipient would actually
    /// receive, which is only possible because the tokens they carry are sealed with keys held by the
    /// application process — no test can read them out of the database.
    /// </summary>
    internal static string MailDropPath { get; } =
        Path.Combine(Path.GetTempPath(), $"identity-access-acceptance-{Guid.NewGuid():N}");

    private static string KeyRingPath { get; } = Path.Combine(MailDropPath, "keys");

    /// <summary>
    /// This run's own database, inside the server container a developer already has running.
    /// <para>
    /// It is not a preference. The Platform bootstrap happens once in a deployment's life, and the journeys that
    /// walk it — the first owner reaching the panel, recovery of an invitation that could not arrive — can only
    /// be walked against a database nobody has bootstrapped. Reusing a shared one makes those scenarios pass
    /// once and then fail forever, and resetting somebody's development database to fix that is not this suite's
    /// to do. A database of its own is the isolation, and it is dropped when the run ends.
    /// </para>
    /// </summary>
    internal static string DatabaseName { get; } = $"acceptance_{Guid.NewGuid():N}";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(8);

    public static IDistributedApplicationTestingBuilder Builder { get; private set; } = null!;
    public static DistributedApplication App { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var cts = new CancellationTokenSource(DefaultTimeout);
        var cancellationToken = cts.Token;

        Builder = await DistributedApplicationTestingBuilder
             .CreateAsync<Projects.AppHost>(
                // Passed as an argument rather than set afterwards: the app host reads its configuration while
                // building the model, so anything assigned after that point arrives too late to be forwarded.
                args:
                [
                    $"--IdentityAccess:Platform:BootstrapOwnerEmail={PlatformBootstrapOwnerEmail}",
                    "--IdentityAccess:Email:Enabled=true",
                    "--IdentityAccess:Email:FromAddress=platform@example.test",
                    // Syntactically what a deployment configures; the journeys rebuild each link on the run's
                    // own frontend, whose port only exists once the host has allocated it.
                    "--IdentityAccess:Email:PublicOrigin=https://localhost",
                    $"--IdentityAccess:Email:LocalDropPath={MailDropPath}",
                    $"--IdentityAccess:Database:Name={DatabaseName}",
                    // The web application seals the tokens and the worker opens them, so they need the same key
                    // ring and the same discriminator. Without it every envelope is unreadable and every message
                    // fails closed — which is the deployment prerequisite EMAIL-SETUP.md states, met locally.
                    "--IdentityAccess:DataProtection:ApplicationName=identity-access-acceptance",
                    $"--IdentityAccess:DataProtection:KeyRingPath={KeyRingPath}",
            // This suite brings its own settings and must not write to the machine's development ones.
            "--IdentityAccess:LocalSetup:Enabled=false",
            "--IdentityAccess:People:DocumentProtection:CurrentKeyVersion=1",
            "--IdentityAccess:People:DocumentProtection:FingerprintKeys:1=YWNjZXB0YW5jZS1maW5nZXJwcmludC1rZXktMzIhISE="
                ],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = false; // Enable the dashboard for testing purposes
                });

        Builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        // A deployment configures one bootstrap owner; the acceptance run configures its own, so the Platform
        // journeys start from the same cold start a real deployment does rather than from seeded authority.
        Builder.Configuration["IdentityAccess:Platform:BootstrapOwnerEmail"] = PlatformBootstrapOwnerEmail;

        Builder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(Builder.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });

        Builder.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        App = await Builder
            .BuildAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await App
            .StartAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await Task.WhenAll(
            App.ResourceNotifications.WaitForResourceHealthyAsync(Services.WebApi, cancellationToken).WaitAsync(cancellationToken),
            App.ResourceNotifications.WaitForResourceHealthyAsync(Services.WebFrontend, cancellationToken).WaitAsync(cancellationToken));

        await AcceptanceTestCredentials.CreateAsync(App, cancellationToken);
    }

    /// <summary>
    /// The last thing the outbox worker said. A journey that follows delivered mail fails as "no file
    /// appeared", which is the symptom; the worker's own log is where the cause is written down.
    /// </summary>
    internal static async Task<string> WorkerLogTailAsync(int lines = 25)
    {
        var loggers = App.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();
        var collected = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var batch in loggers.WatchAsync(Services.OutboxWorker).WithCancellation(timeout.Token))
            {
                collected.AddRange(batch.Select(entry => entry.Content));
                if (collected.Count >= lines) break;
            }
        }
        catch (OperationCanceledException) { /* Whatever arrived in the window is the answer. */ }

        var model = App.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.DistributedApplicationModel>();
        var resources = string.Join(",", model.Resources.Select(resource => resource.Name));
        return collected.Count == 0
            ? $"(no worker log; resources: {resources})"
            : string.Join(" | ", collected.TakeLast(lines));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        var connectionString = await App.GetConnectionStringAsync(Services.Database);
        await App.DisposeAsync();
        // The drop holds live invitation links, so it does not outlive the run that produced them.
        if (Directory.Exists(MailDropPath)) Directory.Delete(MailDropPath, recursive: true);
        await DropRunDatabaseAsync(connectionString);
    }

    /// <summary>
    /// Removes only the database this run created, by name, from the server it created it on. Nothing else on
    /// that server is touched — a developer's own database is somebody else's to keep.
    /// </summary>
    private static async Task DropRunDatabaseAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        try
        {
            var admin = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres",
                Pooling = false,
                CommandTimeout = 60
            };
            await using var connection = new Npgsql.NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);", connection);
            await drop.ExecuteNonQueryAsync();
        }
        catch (Exception failure) when (failure is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // A database left behind is untidy, not wrong, and it is named so it can be found. Failing the run
            // over the cleanup would turn a tidy-up problem into a red suite.
            TestContext.Progress.WriteLine($"The run database {DatabaseName} could not be dropped: {failure.GetType().Name}.");
        }
    }
}
