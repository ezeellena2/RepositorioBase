using Azure.Identity;
using System.Net;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Web.Infrastructure.Identity;
using CleanArchitecture.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddScoped<IUser, CurrentUser>();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        builder.Services.AddSingleton<ApiProblemDetailsMapper>();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Antiforgery.IAntiforgeryAdditionalDataProvider, SessionAntiforgeryAdditionalDataProvider>();
        builder.Services.AddSingleton<CleanArchitecture.Web.Infrastructure.IProblemDetailsService>(provider => provider.GetRequiredService<ApiProblemDetailsMapper>());
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ApiAuthorizationMiddlewareResultHandler>();

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);
        builder.Services.Configure<RouteHandlerOptions>(options =>
            options.ThrowOnBadRequest = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
#if (UseApiOnly)
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
#endif
        });

        builder.Services.AddCors();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-XSRF-TOKEN";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        builder.Services.AddLoginRateLimiting();

        // The ceremony that creates the Platform tenant runs from the host, never from a route.
        builder.Services.AddSingleton<IHostedService, CleanArchitecture.Web.HostedServices.PlatformBootstrapHostedService>();

        // Local mail delivery, and only that. The loop belongs to its own process everywhere else; here it runs
        // in-process so that one process seals the tokens and opens them, which is what makes a local run able
        // to produce a link a person can actually follow.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["IdentityAccess:Email:LocalDropPath"]))
        {
            builder.Services.AddHostedService<CleanArchitecture.Web.HostedServices.LocalOutboxDeliveryService>();
        }
    }

    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder)
    {
        var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential());
        }
    }
}
