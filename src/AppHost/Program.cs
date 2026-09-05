using CleanArchitecture.Shared;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzurePostgresFlexibleServer(Services.DatabaseServer)
    .WithPasswordAuthentication()
    .RunAsContainer(container => 
        container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment()
    // Forwarded rather than read by the web project directly, so a deployment configures the Platform owner in
    // one place. An absent value is absent all the way down: bootstrap then creates nothing (IA-REQ-040).
    // Read through the callback so the value is taken when the resource starts rather than when the model is
    // built — a host that configures it after building would otherwise forward whatever was there first.
    .WithEnvironment(context => context.EnvironmentVariables["IdentityAccess__Platform__BootstrapOwnerEmail"] =
        builder.Configuration["IdentityAccess:Platform:BootstrapOwnerEmail"] ?? string.Empty)
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/scalar";
    });

// The dispatcher runs here rather than inside the web application. Registered there it would also poll from
// inside every functional test that boots the application, racing the rows those tests assert on.
if (!builder.ExecutionContext.IsRunMode || builder.Configuration.GetValue<bool>("IdentityAccess:Email:Enabled"))
{
    builder.AddProject<Projects.OutboxWorker>(Services.OutboxWorker)
        .WithReference(databaseServer)
        .WaitFor(databaseServer);
}

#if (!UseApiOnly)
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddJavaScriptApp(Services.WebFrontend, "./../Web/ClientApp")
        .WithRunScript("start")
        .WithReference(web)
        .WaitFor(web)
        // HTTPS, because the identity design only holds over it: __Host- cookies are refused on an insecure
        // origin, session and antiforgery cookies are issued Secure, and the API compares the browser Origin
        // against the scheme it was reached on. Serving this over HTTP made the proxy hop change the scheme,
        // so every mutation from the SPA was refused as antiforgery_validation_failed.
        .WithHttpsEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}
#endif

builder.Build().Run();
