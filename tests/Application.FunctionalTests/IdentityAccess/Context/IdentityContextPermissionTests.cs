using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Context;

/// <summary>
/// Effective permissions are a persisted projection of the active membership of the selected tenant only
/// (IA-REQ-006/007/008/012). The authentication cookie carries no authorization data, so PostgreSQL is the only
/// source, and another tenant's grants never leak into the answer.
/// </summary>
public sealed class IdentityContextPermissionTests : TestBase
{
    [Test]
    public async Task Identity_context_projects_the_effective_permissions_of_the_active_tenant()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://context-permissions.localhost";
        const string email = "context-permissions@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite, Permissions.MembersRead);
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ShouldBe(
            [Permissions.MembersInvite, Permissions.MembersRead],
            ignoreOrder: true);
    }

    [Test]
    public async Task Identity_context_never_accumulates_permissions_from_another_tenant()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://context-permissions-cross-tenant.localhost";
        const string email = "context-cross-tenant@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var first = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var second = await SeedActiveMembershipAsync(identityId, Permissions.RolesManage, Permissions.TenantRead);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);

        using var selectFirst = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = first.Value }, antiforgery);
        var firstSelection = await client.SendAsync(selectFirst);

        firstSelection.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(firstSelection)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.MembersInvite]);

        var secondAntiforgery = await GetAntiforgeryAsync(client, host);
        using var selectSecond = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = second.Value }, secondAntiforgery);
        var secondSelection = await client.SendAsync(selectSecond);

        secondSelection.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(secondSelection)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.RolesManage, Permissions.TenantRead], ignoreOrder: true);

        var context = await client.GetAsync($"{host}/api/identity/context");
        context.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(context)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.RolesManage, Permissions.TenantRead], ignoreOrder: true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Identity_context_returns_no_permissions_when_the_tenant_or_membership_is_suspended(bool suspendTenant)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var scope = suspendTenant ? "tenant" : "membership";
        var host = $"https://context-permissions-suspended-{scope}.localhost";
        var email = $"context-suspended-{scope}@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(tenantId);
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // The baseline is asserted first, so an implementation that simply always answers with an empty list
        // cannot satisfy this test: the permission has to be there before the suspension takes it away.
        var granted = await client.GetAsync($"{host}/api/identity/context");
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(granted)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.MembersInvite]);

        if (suspendTenant) await SuspendTenantAsync(tenantId); else await SuspendMembershipAsync(tenantId);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("activeTenant").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Identity_context_excludes_permissions_granted_only_through_a_retired_role()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://context-permissions-retired.localhost";
        const string email = "context-retired@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var granted = await client.GetAsync($"{host}/api/identity/context");
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(granted)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.MembersInvite], "the grant must exist before retiring the role can remove it");

        await RetireRolesAsync(tenantId);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("activeTenant").GetProperty("id").GetGuid().ShouldBe(tenantId.Value, "the membership survives; only the role is retired");
        body.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Identity_context_returns_no_permissions_without_an_active_tenant_selection()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://context-permissions-unselected.localhost";
        const string email = "context-unselected@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var first = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBeNull("two active memberships require an explicit selection");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unselected = await ReadJsonAsync(response);
        unselected.GetProperty("availableTenants").GetArrayLength().ShouldBe(2);
        unselected.GetProperty("permissions").GetArrayLength().ShouldBe(0);

        // Selecting one of them proves the empty answer above came from the missing selection, not from an
        // implementation that never projects anything.
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var select = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = first.Value }, antiforgery);
        var selected = await client.SendAsync(select);

        selected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(selected)).GetProperty("permissions").EnumerateArray().Select(value => value.GetString())
            .ShouldBe([Permissions.MembersInvite]);
    }
}
