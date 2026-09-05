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
                args: [$"--IdentityAccess:Platform:BootstrapOwnerEmail={PlatformBootstrapOwnerEmail}"],
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

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await App.DisposeAsync();
    }
}
