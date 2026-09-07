using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Drives the real identity endpoints over the production pipeline: no test authentication handler, no identity
/// access doubles, and the same cookie, antiforgery and PostgreSQL behaviour a browser would meet.
/// </summary>
internal static class IdentityHttpHarness
{
    internal static ProductionHarness CreateProductionHarness(
        TimeProvider? timeProvider = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        var factory = new WebApiFactory(
            FunctionalTestSetup.ConnectionString,
            Microsoft.Extensions.Hosting.Environments.Production,
            useTestAuthentication: false,
            useTestIdentityAccessDoubles: false,
            timeProvider: timeProvider,
            settings: settings,
            configureTestServices: configureTestServices);
        return new ProductionHarness(factory, factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false }));
    }

    internal static async Task<string> GetAntiforgeryAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
    }

    internal static HttpRequestMessage JsonRequest(HttpMethod method, string uri, object? body, string antiforgery)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Origin", new Uri(uri).GetLeftPart(UriPartial.Authority));
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        return request;
    }

    internal static HttpRequestMessage LoginRequest(string host, string email, string password, string antiforgery, string? forwardedFor = null)
    {
        var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery);
        // TestServer reports no remote address; the trusted first hop of X-Forwarded-For simulates the client address.
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    internal static async Task<string> SignInAsync(HttpClient client, string host, string email, string password)
    {
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    /// <summary>Reads an RFC 9457 body, asserting the media type that makes it one.</summary>
    internal static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    internal static async Task<Guid> SeedConfirmedUserAsync(string email, string password)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // "Confirmed" now means a state as well as a flag, and this helper's whole job is to be the premise
        // "an account somebody can sign into" (IA-REQ-054).
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Status = CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus.Active
        };
        (await users.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
        return user.Id;
    }

    /// <summary>Synchronizes the code-owned permission catalog that Respawn removes before every test.</summary>
    internal static async Task SeedPermissionCatalogAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PermissionCatalogSynchronizer>().SynchronizeAsync(CancellationToken.None);
    }

    internal static async Task<TenantId> SeedActiveMembershipAsync(Guid identityId, params string[] permissionCodes)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"harness-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        membership.Activate(tenant);
        context.AddRange(tenant, membership);

        if (permissionCodes.Length > 0)
        {
            var role = Role.Create(tenant, $"role-{Guid.NewGuid():N}");
            context.Add(role);
            context.Add(MembershipRole.Create(tenant, membership, role));
            foreach (var code in permissionCodes)
            {
                var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == code);
                context.Add(RolePermission.Create(tenant, role, permission));
            }
        }

        await context.SaveChangesAsync();
        return tenant.Id;
    }

    internal static async Task RetireRolesAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        foreach (var role in await context.TenantRoles.Where(candidate => candidate.TenantId == tenantId).ToListAsync())
        {
            role.Retire(tenant);
        }

        await context.SaveChangesAsync();
    }

    internal static async Task SuspendTenantAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        tenant.Suspend(TenantSuspensionReason.OperatorRequest, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    internal static async Task SuspendMembershipAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        var membership = await context.TenantMemberships.SingleAsync(candidate => candidate.TenantId == tenantId);
        membership.Suspend(tenant);
        await context.SaveChangesAsync();
    }

    internal static async Task<UserSession> GetOnlySessionAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserSessions.AsNoTracking().SingleAsync();
    }

    internal static async Task<ApplicationUser> GetUserAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == identityId);
    }

    internal static async Task<int> CountAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<TEntity>().CountAsync();
    }

    internal static async Task<List<TEntity>> ListAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<TEntity>().AsNoTracking().ToListAsync();
    }

    internal sealed class ProductionHarness(WebApiFactory factory, HttpClient client) : IDisposable
    {
        public WebApiFactory Factory { get; } = factory;

        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    internal sealed class ControlledTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
