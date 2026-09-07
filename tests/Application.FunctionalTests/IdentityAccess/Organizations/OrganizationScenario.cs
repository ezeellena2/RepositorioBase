using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

/// <summary>
/// One `Organization` with a real owner, and the ability to add members holding exactly the codes a case needs.
/// <para>
/// The owner's authority is the authority a registration actually produces — the system role with the codes
/// amendment D1 decided it holds — rather than whatever a fixture felt like granting. That distinction is the
/// whole point: C5's ceiling is a statement about what an actor really has, so a test that granted itself the
/// answer would be testing nothing.
/// </para>
/// </summary>
internal sealed class OrganizationScenario : IDisposable
{
    internal const string Password = "Testing1234!";

    private readonly IdentityHttpHarness.ProductionHarness _harness;
    private readonly string _host;

    private OrganizationScenario(IdentityHttpHarness.ProductionHarness harness, string host, TenantId tenantId, Guid ownerId, Guid ownerMembershipId, Administrator owner)
    {
        _harness = harness;
        _host = host;
        TenantId = tenantId;
        OwnerId = ownerId;
        OwnerMembershipId = ownerMembershipId;
        Owner = owner;
    }

    internal TenantId TenantId { get; }

    internal Guid OwnerId { get; }

    internal Guid OwnerMembershipId { get; }

    internal Administrator Owner { get; }

    internal static async Task<OrganizationScenario> CreateAsync(string label = "org")
    {
        await IdentityHttpHarness.SeedPermissionCatalogAsync();
        var harness = IdentityHttpHarness.CreateProductionHarness();
        var host = $"https://{label}-{Guid.NewGuid():N}.localhost";
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var ownerId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        var (tenantId, membershipId) = await SeedOwnerTenantAsync(ownerId);
        var owner = await Administrator.SignInAsync(harness, host, email, tenantId);
        return new OrganizationScenario(harness, host, tenantId, ownerId, membershipId, owner);
    }

    /// <summary>
    /// An organization owned the way `RegistrationInitialRoleProvisioner` and the confirmation path own one: the
    /// system role, the assignment, the codes D1 names, and the tenant's one ownership reference.
    /// </summary>
    private static async Task<(TenantId TenantId, Guid MembershipId)> SeedOwnerTenantAsync(Guid ownerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"scenario-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, ownerId);
        membership.Activate(tenant);
        var role = Role.CreateSystem(tenant, "Owner");
        context.AddRange(tenant, membership, role, MembershipRole.Create(tenant, membership, role));
        foreach (var permission in await context.Permissions.Where(candidate => Permissions.OrganizationOwnerCodes.Contains(candidate.Code)).ToListAsync())
        {
            context.Add(RolePermission.Create(tenant, role, permission));
        }

        // The tenant points at the membership and the membership at the tenant, so the rows go first and the
        // owner is named second — the same two steps the real registration takes, for the same reason.
        await context.SaveChangesAsync();
        tenant.TransferOwnershipTo(membership);
        await context.SaveChangesAsync();
        return (tenant.Id, membership.Id.Value);
    }

    /// <summary>Adds an active member holding exactly these codes, through a role of their own, and signs them in.</summary>
    internal async Task<SignedInMember> AddMemberAsync(string label, params string[] codes)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        var membershipId = await SeedMembershipAsync(identityId, $"{label}-role", codes);
        return new SignedInMember(await Administrator.SignInAsync(_harness, _host, email, TenantId), membershipId, identityId);
    }

    /// <summary>A member who never signs in. Enough for a case that only administers them.</summary>
    internal async Task<Guid> AddQuietMemberAsync(string label, params string[] codes)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        return await SeedMembershipAsync(identityId, $"{label}-role", codes);
    }

    private async Task<Guid> SeedMembershipAsync(Guid identityId, string roleName, string[] codes)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == TenantId);
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        membership.Activate(tenant);
        var role = Role.Create(tenant, roleName);
        context.AddRange(membership, role, MembershipRole.Create(tenant, membership, role));
        foreach (var permission in await context.Permissions.Where(candidate => codes.Contains(candidate.Code)).ToListAsync())
        {
            context.Add(RolePermission.Create(tenant, role, permission));
        }

        await context.SaveChangesAsync();
        return membership.Id.Value;
    }

    /// <summary>Takes the owner's own administration away, so a custom role becomes the only source of it.</summary>
    internal async Task RetireOwnerMembershipRoleAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await context.TenantMemberships.SingleAsync(candidate => candidate.TenantId == TenantId && candidate.IdentityId == OwnerId);
        var links = await context.MembershipRoles.Where(link => link.TenantId == TenantId && link.MembershipId == membership.Id).ToListAsync();
        context.MembershipRoles.RemoveRange(links);
        await context.SaveChangesAsync();
    }

    internal async Task InviteAsync(string email, Guid roleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == TenantId);
        var role = await context.TenantRoles.SingleAsync(candidate => candidate.TenantId == TenantId && candidate.Id == RoleId.From(roleId));
        context.Add(Invitation.Issue(
            tenant,
            email,
            [role],
            VersionedTokenHash.Of($"token-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7)));
        await context.SaveChangesAsync();
    }

    public void Dispose() => _harness.Dispose();
}

/// <summary>A member a case can both act as and act on, so neither has to be looked up afterwards.</summary>
internal sealed record SignedInMember(Administrator Acting, Guid MembershipId, Guid IdentityId);

/// <summary>One signed-in administrator, carrying its own cookie and buying its own proofs.</summary>
internal sealed class Administrator(HttpClient client, string host, string cookie, TenantId tenantId)
{
    internal TenantId TenantId { get; } = tenantId;

    internal static async Task<Administrator> SignInAsync(IdentityHttpHarness.ProductionHarness harness, string host, string email, TenantId tenantId)
    {
        // No cookie jar: a case that signs two people in must not have one client quietly answering for the other.
        var client = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = OrganizationScenario.Password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))
            .Split(';')[0];
        return new Administrator(client, host, cookie, tenantId);
    }

    /// <summary>Spends a password for the single-use proof a sensitive change requires (IA-REQ-051).</summary>
    internal async Task ProveAsync(string action)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/reauthenticate", new { action, password = OrganizationScenario.Password }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(method, $"{host}{path}", body, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        return await client.SendAsync(request);
    }

    internal async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}{path}");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    internal async Task<T> ReadAsync<T>(string path)
    {
        var response = await GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(string Cookie, string Token)> AntiforgeryAsync(HttpClient client, string host, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal))
            .Split(';')[0];
        return (pair, token);
    }
}
