using System.Net;
using System.Text.RegularExpressions;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;

public sealed class HealthEndpointTests
{
    [TestCase("ready", "/health", HttpStatusCode.ServiceUnavailable)]
    [TestCase("ready", "/alive", HttpStatusCode.OK)]
    [TestCase("live", "/health", HttpStatusCode.OK)]
    [TestCase("live", "/alive", HttpStatusCode.ServiceUnavailable)]
    public async Task Production_probes_are_status_only_and_isolate_ready_from_live(
        string failingTag,
        string path,
        HttpStatusCode expectedStatus)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.AddDefaultHealthChecks();
        builder.Services.AddHealthChecks().AddCheck(
            $"{failingTag}-failure",
            () => HealthCheckResult.Unhealthy(),
            tags: [failingTag]);
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.ShouldBe(expectedStatus);
        response.Content.Headers.ContentType.ShouldBeNull();
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public void Infrastructure_registers_database_as_ready_and_self_as_live_only()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:CleanArchitectureDb"] =
            "Host=localhost;Database=health-contract;Username=health;Password=health";
        builder.AddDefaultHealthChecks();
        builder.AddInfrastructureServices();
        using var provider = builder.Services.BuildServiceProvider();

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        var database = registrations.Single(registration => registration.Name == "database");
        database.FailureStatus.ShouldBe(HealthStatus.Unhealthy);
        database.Tags.ShouldBe(["ready"], ignoreOrder: true);
        var self = registrations.Single(registration => registration.Name == "self");
        self.Tags.ShouldContain("live");
        self.Tags.ShouldNotContain("ready");
    }

    [Test]
    public void Worker_and_apphost_expose_internal_health_without_dropping_hosted_services()
    {
        var workerProject = File.ReadAllText(RepositoryFile("src/OutboxWorker/OutboxWorker.csproj"));
        var workerProgram = File.ReadAllText(RepositoryFile("src/OutboxWorker/Program.cs"));
        var appHost = File.ReadAllText(RepositoryFile("src/AppHost/Program.cs"));

        workerProject.ShouldContain("<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        workerProgram.ShouldContain("WebApplication.CreateBuilder(args)");
        workerProgram.ShouldContain("app.MapDefaultEndpoints()");
        workerProgram.ShouldContain("AddHostedService<Worker>()");
        workerProgram.ShouldContain("AddHostedService<IdleAnnouncement>()");
        workerProgram.ShouldContain("AddHostedService<CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle.LifecycleMaintenanceService>()");

        Regex.Matches(appHost, "\\.WithHttpHealthCheck\\(\"/health\"\\)").Count.ShouldBe(2);
        var workerResourceStart = appHost.IndexOf("builder.AddProject<Projects.OutboxWorker>", StringComparison.Ordinal);
        var workerResourceEnd = appHost.IndexOf(';', workerResourceStart);
        var workerResource = appHost[workerResourceStart..workerResourceEnd];
        workerResource.ShouldContain(".WithHttpEndpoint(targetPort: 8080, name: \"http\")");
        workerResource.ShouldNotContain(".WithExternalHttpEndpoints()");
    }

    [Test]
    public void Entity_framework_health_dependency_belongs_to_infrastructure()
    {
        const string package = "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore";
        File.ReadAllText(RepositoryFile("src/Infrastructure/Infrastructure.csproj")).ShouldContain(package);
        File.ReadAllText(RepositoryFile("src/Web/Web.csproj")).ShouldNotContain(package);
    }

    private static string RepositoryFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
