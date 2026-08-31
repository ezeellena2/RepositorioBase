using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests;

[SetUpFixture]
public sealed class InfrastructureTestSetup
{
    private static DistributedApplication? _app;
    private static WebApiFactory? _factory;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cancellationSource.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(
            args: [],
            configureBuilder: static (options, _) => options.DisableDashboard = true);

        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder.BuildAsync(cancellationToken).WaitAsync(cancellationToken);
        await _app.StartAsync(cancellationToken).WaitAsync(cancellationToken);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(Services.Database, cancellationToken);

        var connectionString = (await _app.GetConnectionStringAsync(Services.Database))!;
        _factory = new WebApiFactory(connectionString);
        TestServices.Configure(_factory.Services.GetRequiredService<IServiceScopeFactory>());
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
