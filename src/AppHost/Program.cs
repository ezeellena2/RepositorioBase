using CleanArchitecture.Shared;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// A local run needs a key ring that persists, a folder for mail, and a fingerprint key that has no default by
// design. They are filled in once, into user secrets outside the repository, and never overwritten.
CleanArchitecture.AppHost.LocalDevelopmentSetup.EnsureConfigured(builder);

builder.AddAzureContainerAppEnvironment("aca-env");

// The server container is persistent, so a developer's data survives a restart. The database inside it can
// still be named per run: some ceremonies happen once in a deployment's life — the Platform bootstrap is one —
// and walking those from a cold start needs a database nobody has bootstrapped, not a reset of the one somebody
// is using. An unnamed run keeps the shared default.
var databaseName = builder.Configuration["IdentityAccess:Database:Name"];
var databaseServer = builder
    .AddAzurePostgresFlexibleServer(Services.DatabaseServer)
    .WithPasswordAuthentication()
    .RunAsContainer(container => 
        container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database, string.IsNullOrWhiteSpace(databaseName) ? null : databaseName);

var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithAspNetCoreEnvironment()
    // Forwarded rather than read by the web project directly, so a deployment configures the Platform owner in
    // one place. An absent value is absent all the way down: bootstrap then creates nothing (IA-REQ-040).
    // Read through the callback so the value is taken when the resource starts rather than when the model is
    // built — a host that configures it after building would otherwise forward whatever was there first.
    .WithEnvironment(context => context.EnvironmentVariables["IdentityAccess__Platform__BootstrapOwnerEmail"] =
        builder.Configuration["IdentityAccess:Platform:BootstrapOwnerEmail"] ?? string.Empty)
    .WithEnvironment(ForwardEmailSettings)
    .WithEnvironment(ForwardLocalizationSettings)
    // Only the web application records documents, so only it needs the fingerprint keys. Forwarding them to the
    // worker as well would spread key material to a process that has no use for it.
    .WithEnvironment(ForwardDocumentProtectionSettings)
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/swagger";
    });

// The dispatcher runs here rather than inside the web application. Registered there it would also poll from
// inside every functional test that boots the application, racing the rows those tests assert on.
//
// A local drop folder is the exception: the web application delivers those itself, so that one process seals
// the tokens and opens them. Starting this worker as well would mean sharing a key ring between two processes
// to accomplish nothing a developer asked for.
if (!builder.ExecutionContext.IsRunMode ||
    (builder.Configuration.GetValue<bool>("IdentityAccess:Email:Enabled") &&
     string.IsNullOrWhiteSpace(builder.Configuration["IdentityAccess:Email:LocalDropPath"])))
{
    builder.AddProject<Projects.OutboxWorker>(Services.OutboxWorker)
        .WithReference(databaseServer)
        .WaitFor(databaseServer)
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithHttpHealthCheck("/health")
        .WithAspNetCoreEnvironment()
        .WithEnvironment(ForwardEmailSettings)
        .WithEnvironment(ForwardLocalizationSettings);
}

// The email and key-protection settings live in one place and reach both processes from it.
//
// Both halves matter. The worker renders the links and the web application issues the tokens they carry,
// so the two disagreeing about the public origin would produce mail nobody can act on — and the tokens
// are sealed by one process and opened by the other, so without a shared key ring and discriminator every
// envelope is unreadable and every message fails closed. That is the prerequisite EMAIL-SETUP.md states
// for a deployment; forwarding it here is what lets a local run deliver at all.
void ForwardEmailSettings(EnvironmentCallbackContext context)
{
    foreach (var key in new[] { "Enabled", "FromAddress", "PublicOrigin", "LocalDropPath" })
    {
        if (builder.Configuration[$"IdentityAccess:Email:{key}"] is { Length: > 0 } value)
        {
            context.EnvironmentVariables[$"IdentityAccess__Email__{key}"] = value;
        }
    }

    foreach (var key in new[] { "ApplicationName", "KeyRingPath" })
    {
        if (builder.Configuration[$"IdentityAccess:DataProtection:{key}"] is { Length: > 0 } value)
        {
            context.EnvironmentVariables[$"IdentityAccess__DataProtection__{key}"] = value;
        }
    }
}

// Web request negotiation and background delivery must share one deployment fallback. Forwarding the same value
// prevents a customized host default from silently reverting to English in the worker.
void ForwardLocalizationSettings(EnvironmentCallbackContext context)
{
    if (builder.Configuration["Localization:DefaultLanguage"] is { Length: > 0 } language)
    {
        context.EnvironmentVariables["Localization__DefaultLanguage"] = language;
    }
}

// The fingerprint keys a person's documentary identity is looked up by. There is deliberately no default: a
// digest under a key everybody knows is not keyed, and a development default would silently become a production
// one. A deployment that configures none simply cannot record a document, which is the safe way to be wrong.
void ForwardDocumentProtectionSettings(EnvironmentCallbackContext context)
{
    if (builder.Configuration["IdentityAccess:People:DocumentProtection:CurrentKeyVersion"] is { Length: > 0 } version)
    {
        context.EnvironmentVariables["IdentityAccess__People__DocumentProtection__CurrentKeyVersion"] = version;
    }

    foreach (var key in builder.Configuration.GetSection("IdentityAccess:People:DocumentProtection:FingerprintKeys").GetChildren())
    {
        if (key.Value is { Length: > 0 } material)
        {
            context.EnvironmentVariables[$"IdentityAccess__People__DocumentProtection__FingerprintKeys__{key.Key}"] = material;
        }
    }
}

#if (!UseApiOnly)
if (builder.ExecutionContext.IsRunMode)
{
    var frontend = builder.AddJavaScriptApp(Services.WebFrontend, "./../Web/ClientApp")
        .WithRunScript("start")
        .WithReference(web)
        .WaitFor(web)
        // HTTPS, because the identity design only holds over it: __Host- cookies are refused on an insecure
        // origin, session and antiforgery cookies are issued Secure, and the API compares the browser Origin
        // against the scheme it was reached on. Serving this over HTTP made the proxy hop change the scheme,
        // so every mutation from the SPA was refused as antiforgery_validation_failed.
        .WithHttpsEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();

    // Locally, the origin the links point at is this frontend, and its port does not exist until the host has
    // allocated it — so it cannot be configured in advance, and a link rendered on a guessed one is a link that
    // opens nothing. A value that was configured is left alone: a deployment names its own public origin, and
    // this fills in only where nobody could have.
    if (string.IsNullOrWhiteSpace(builder.Configuration["IdentityAccess:Email:PublicOrigin"]))
    {
        web.WithEnvironment("IdentityAccess__Email__PublicOrigin", frontend.GetEndpoint("https"));
    }
}
#endif

builder.Build().Run();
