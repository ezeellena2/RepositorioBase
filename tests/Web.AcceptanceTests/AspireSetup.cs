using Aspire.Hosting;

namespace CleanArchitecture.Web.AcceptanceTests;

[SetUpFixture]
public class AspireSetup
{
    /// <summary>
    /// How long the whole distributed application has to become healthy. It covers a container start, a
    /// migration, a permission-catalogue synchronization and a Vite dev server, and the budget is spent while
    /// the rest of the solution's suites are competing for the same machine — a minute is comfortable when this
    /// project runs alone and not when it does not, which is exactly the flake that looks like a broken app.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public static IDistributedApplicationTestingBuilder Builder { get; private set; } = null!;
    public static DistributedApplication App { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var cts = new CancellationTokenSource(DefaultTimeout);
        var cancellationToken = cts.Token;

        Builder = await DistributedApplicationTestingBuilder
             .CreateAsync<Projects.AppHost>(
                args: [],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = false; // Enable the dashboard for testing purposes
                });

        Builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

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
