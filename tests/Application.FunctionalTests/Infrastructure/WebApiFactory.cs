using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(string connectionString, string? environmentName = null, bool useTestAuthentication = true, bool useStaleApplicationUser = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CleanArchitectureDb", connectionString);
        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder.UseEnvironment(environmentName);
        }

        builder.ConfigureTestServices(services =>
        {
            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                services.RemoveAll<IHostedService>();
            }
            if (useTestAuthentication)
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                });
                services.PostConfigure<AuthorizationOptions>(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .RequireClaim(TestAuthenticationHandler.PermissionClaim, TestAuthenticationHandler.PermissionValue)
                        .Build();
                });
            }
            services
                .RemoveAll<IUser>()
                .AddTransient(provider =>
                {
                    var mock = new Mock<IUser>();
                    mock.SetupGet(x => x.Roles).Returns(TestApp.GetRoles());
                    mock.SetupGet(x => x.Id).Returns(useStaleApplicationUser ? null : TestApp.GetUserId());
                    return mock.Object;
                });
            services.RemoveAll<ICurrentTenant>();
            services.AddScoped<ICurrentTenant, TestCurrentTenant>();
            services.RemoveAll<IPermissionEvaluator>();
            services.AddScoped<IPermissionEvaluator, TestPermissionEvaluator>();
            services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor, Task6TestSaveChangesInterceptor>();
        });
    }

    private sealed class TestCurrentTenant : ICurrentTenant
    {
        public TenantId? TenantId => TestApp.GetTenantId();
    }

    private sealed class TestPermissionEvaluator : IPermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(Guid identityId, string permissionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(identityId != Guid.Empty && TestApp.IsApplicationPermissionGranted());

        public Task<bool> HasPermissionAsync(Guid identityId, TenantId tenantId, string permissionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(identityId != Guid.Empty && !tenantId.IsEmpty && TestApp.IsApplicationPermissionGranted());
    }
}
