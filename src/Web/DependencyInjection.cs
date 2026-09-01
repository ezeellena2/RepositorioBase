using Azure.Identity;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Web.Services;
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
            options.AddOperationTransformer<IdentityApiOperationTransformer>();
#if (UseApiOnly)
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
#endif
        });

        builder.Services.AddCors();
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
