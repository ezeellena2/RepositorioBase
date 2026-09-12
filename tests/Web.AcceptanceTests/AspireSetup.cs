using Aspire.Hosting;
using System.Diagnostics;
using System.Net;

namespace CleanArchitecture.Web.AcceptanceTests;

[SetUpFixture]
public class AspireSetup
{
    /// <summary>
    /// How long the whole distributed application has to become ready. It covers a container start, a
    /// migration, a permission-catalogue synchronization and a Vite dev server. Readiness is proved below by
    /// the database and the two endpoints the journeys actually use; this is only their shared diagnostic
    /// ceiling, so a fast start costs none of it.
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
    private static readonly TimeSpan ProbeAttemptTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(2);

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

        var orchestration = Stopwatch.StartNew();
        try
        {
            await App
                .StartAsync(cancellationToken)
                .WaitAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            throw StartupFailure(
                "Aspire orchestration",
                "AppHost",
                orchestration.Elapsed,
                failure.GetType().Name);
        }

        await WaitForApplicationReadinessAsync(cancellationToken);

        await AcceptanceTestCredentials.CreateAsync(App, cancellationToken);
    }

    private static async Task WaitForApplicationReadinessAsync(CancellationToken cancellationToken)
    {
        await WaitForConditionAsync(
            "PostgreSQL accepts a query",
            Services.Database,
            ProbeDatabaseAsync,
            cancellationToken);
        await WaitForConditionAsync(
            "Web API identity bootstrap endpoint answers successfully",
            Services.WebApi,
            token => ProbeEndpointAsync(Services.WebApi, "/api/identity/antiforgery", token),
            cancellationToken);
        await WaitForConditionAsync(
            "SPA root document answers successfully",
            Services.WebFrontend,
            token => ProbeEndpointAsync(Services.WebFrontend, "/", token),
            cancellationToken);
    }

    private static async Task WaitForConditionAsync(
        string condition,
        string resource,
        Func<CancellationToken, Task<StartupProbeResult>> probe,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var lastSafeObservation = "not attempted";

        while (!cancellationToken.IsCancellationRequested)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(ProbeAttemptTimeout);
            try
            {
                var result = await probe(attempt.Token);
                lastSafeObservation = result.Observation;
                if (result.Ready)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                // Only the exception type is retained. Messages, response bodies and request data may contain
                // tokens or personal data and are never startup diagnostics.
                lastSafeObservation = failure.GetType().Name;
            }

            try
            {
                await Task.Delay(ProbeInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw StartupFailure(condition, resource, elapsed.Elapsed, lastSafeObservation);
    }

    private static async Task<StartupProbeResult> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = await App.GetConnectionStringAsync(Services.Database, cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(false, "connection string unavailable");
        }

        var options = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = (int)ProbeAttemptTimeout.TotalSeconds,
            CommandTimeout = (int)ProbeAttemptTimeout.TotalSeconds,
            Pooling = false
        };
        await using var connection = new Npgsql.NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("SELECT 1;", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Equals(result, 1)
            ? new(true, "query succeeded")
            : new(false, "query returned an unexpected scalar type");
    }

    private static async Task<StartupProbeResult> ProbeEndpointAsync(
        string resource,
        string path,
        CancellationToken cancellationToken)
    {
        var endpoint = App.GetEndpoint(resource);
        if (!endpoint.IsLoopback)
        {
            return new(false, "endpoint is not loopback");
        }

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = endpoint,
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var status = (int)response.StatusCode;
        return status is >= (int)HttpStatusCode.OK and < (int)HttpStatusCode.MultipleChoices
            ? new(true, $"HTTP {status}")
            : new(false, $"HTTP {status}");
    }

    private static InvalidOperationException StartupFailure(
        string condition,
        string resource,
        TimeSpan elapsed,
        string lastSafeObservation) =>
        new(
            $"Acceptance startup condition '{condition}' for resource '{resource}' failed after " +
            $"{elapsed.TotalSeconds:F1}s. Last safe observation: {lastSafeObservation}. " +
            $"Process context: pid={Environment.ProcessId}; required resources=" +
            $"{Services.Database},{Services.WebApi},{Services.WebFrontend}.");

    private readonly record struct StartupProbeResult(bool Ready, string Observation);

    /// <summary>
    /// Safe delivery-process output context. Local-drop delivery runs inside the web application, not the
    /// outbox worker. Only counts and stream metadata leave this method: log contents can carry addresses,
    /// tokens or payloads and are never copied into test failures.
    /// </summary>
    internal static async Task<string> WorkerLogTailAsync()
    {
        var loggers = App.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();
        var lineCount = 0;
        var errorCount = 0;
        var lastLineNumber = 0;
        using var diagnosticTimeout = new CancellationTokenSource(DiagnosticTimeout);
        try
        {
            await foreach (var batch in loggers
                               .GetAllAsync(Services.WebApi)
                               .WithCancellation(diagnosticTimeout.Token))
            {
                foreach (var entry in batch)
                {
                    lineCount++;
                    if (entry.IsErrorMessage) errorCount++;
                    lastLineNumber = Math.Max(lastLineNumber, entry.LineNumber);
                }
            }
        }
        catch (OperationCanceledException) when (diagnosticTimeout.IsCancellationRequested)
        {
            // Partial metadata is still useful; diagnostic collection must never extend the failed journey.
        }

        var model = App.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.DistributedApplicationModel>();
        var resources = string.Join(",", model.Resources.Select(resource => resource.Name));
        return $"resource={Services.WebApi}; lines={lineCount}; stderr={errorCount}; " +
               $"last-line={lastLineNumber}; resources={resources}";
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
