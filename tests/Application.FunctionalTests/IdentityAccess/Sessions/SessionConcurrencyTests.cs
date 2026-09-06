using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Web.Infrastructure;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Sessions;

/// <summary>
/// A session is a concurrency-token protected row (IA-REQ-035). Losing a race with another request is expected
/// behaviour, never an unexpected failure: every outcome is a typed contract, an already invalid session always
/// fails closed, and no generic 500 ever escapes.
/// </summary>
public sealed class SessionConcurrencyTests : TestBase
{
    [Test]
    public async Task Revoking_a_session_that_lost_a_concurrent_revocation_fails_closed_and_still_deletes_the_cookie()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-revoke-revoke.localhost";
        const string email = "race-revoke-revoke@example.test";
        await SeedConfirmedUserAsync(email, "Testing1234!");
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionRevoke(SessionWriteStage.Revocation);
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionRevoke.ShouldBeFalse("the competing revocation must fire while this revocation is persisted");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        (await GetOnlySessionAsync()).RevokedAt.ShouldNotBeNull();
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked")
            .ShouldBe(0, "this request revoked nothing, so it must not claim a revocation it did not perform");
        response.Headers.GetValues("Set-Cookie").ShouldContain(value =>
            value.Contains("__Host-ia-auth=", StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("path=/", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task Revoking_a_session_that_lost_a_concurrent_tenant_selection_still_revokes_it_exactly_once()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-revoke-select.localhost";
        const string email = "race-revoke-select@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var competingTenant = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionSelection(SessionWriteStage.Revocation, competingTenant);
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("the competing selection must fire while this revocation is persisted");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldNotBeNull();
        session.ActiveTenantId.ShouldBeNull("a revoked session keeps no active tenant");
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(1);
        (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Selecting_a_tenant_that_lost_a_concurrent_revocation_fails_closed_without_activating_it()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-select-revoke.localhost";
        const string email = "race-select-revoke@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionRevoke(SessionWriteStage.TenantSelection);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = tenantId.Value }, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionRevoke.ShouldBeFalse("the competing revocation must fire while this selection is persisted");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldNotBeNull();
        session.ActiveTenantId.ShouldBeNull();
    }

    [Test]
    public async Task Concurrent_tenant_selections_settle_on_one_coherent_active_tenant_and_permission_set()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-select-select.localhost";
        const string email = "race-select-select@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var requested = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var competing = await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionSelection(SessionWriteStage.TenantSelection, competing);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = requested.Value }, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("the competing selection must fire while this selection is persisted");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("activeTenant").GetProperty("id").GetGuid().ShouldBe(requested.Value);
        body.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ShouldBe([Permissions.MembersInvite]);
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(requested, "the committed row must agree with the answer this request returned");
    }

    [Test]
    public async Task Revoking_a_session_that_never_wins_its_write_returns_the_typed_conflict_and_keeps_the_cookie()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-revoke-exhausted.localhost";
        const string email = "race-revoke-exhausted@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var first = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var second = await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        // One competing selection per attempt the handler has, each one changing the active tenant so it really
        // rotates the concurrency token and the revocation can never land.
        TestApp.EnableConcurrentSessionSelections(SessionWriteStage.Revocation, first, second, first);
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("every armed competing selection must have fired");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();
        problem!.Code.ShouldBe("session_concurrency_conflict");
        problem.Status.ShouldBe((int)HttpStatusCode.Conflict);
        problem.TraceId.ShouldNotBeNullOrWhiteSpace();
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldBeNull("the session is still live, so the caller may retry");
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse("a live session keeps its cookie for the retry the conflict invites");
    }

    [Test]
    public async Task Selecting_a_tenant_that_never_wins_its_write_returns_the_typed_conflict_without_a_partial_change()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-select-exhausted.localhost";
        const string email = "race-select-exhausted@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var requested = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var competingFirst = await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        var competingSecond = await SeedActiveMembershipAsync(identityId, Permissions.TenantRead);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionSelections(SessionWriteStage.TenantSelection, competingFirst, competingSecond, competingFirst);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = requested.Value }, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("every armed competing selection must have fired");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.Code.ShouldBe("session_concurrency_conflict");
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldBeNull();
        session.ActiveTenantId.ShouldBe(competingFirst, "only the last committed competing selection survives");
    }

    [Test]
    public async Task Signing_in_at_the_cap_while_a_parallel_sign_out_revokes_the_oldest_still_creates_the_new_session()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-signin-supersede.localhost";
        const string email = "race-signin-supersede@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");

        // The eviction only happens at the cap, so the race needs the cap. Before C2 every sign-in revoked every
        // other session and this race was reachable with two; IA-REQ-049 moved it to the sixth.
        var firstCookie = await SignInAsync(client, host, email, "Testing1234!");
        for (var attempt = 0; attempt < 4; attempt++) await SignInAsync(client, host, email, "Testing1234!");
        TestApp.EnableConcurrentSessionRevoke(SessionWriteStage.Eviction);

        var sixthCookie = await SignInAsync(client, host, email, "Testing1234!");

        TestApp.HasPendingConcurrentSessionRevoke.ShouldBeFalse("the competing sign-out must fire while the eviction is persisted");
        sixthCookie.ShouldNotBe(firstCookie);
        var sessions = await ListAsync<UserSession>();
        sessions.Count.ShouldBe(6);
        sessions.Count(session => session.RevokedAt is null).ShouldBe(5, "the cap holds: five live, never six");
        sessions.ShouldAllBe(session => session.IdentityId == identityId);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked")
            .ShouldBe(0, "the competing sign-out performed the revocation, so this sign-in must not claim it");
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.succeeded").ShouldBe(6);

        using var staleClient = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        staleClient.DefaultRequestHeaders.Add("Cookie", firstCookie);
        (await staleClient.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Evicting_a_prior_session_advances_its_concurrency_token_past_a_parallel_tenant_selection()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-supersede-token.localhost";
        const string email = "race-supersede-token@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var competingTenant = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var prior = (await ListAsync<UserSession>()).Single();
        prior.ActiveTenantId.ShouldBeNull("two active memberships suppress the automatic selection");
        for (var attempt = 0; attempt < 4; attempt++) await SignInAsync(client, host, email, "Testing1234!");

        // The competing selection rotates the token without revoking, exactly between the moment the sign-in reads
        // the oldest session and the moment it writes the eviction.
        TestApp.EnableConcurrentSessionSelection(SessionWriteStage.Eviction, competingTenant);

        await SignInAsync(client, host, email, "Testing1234!");

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("the competing selection must fire while the eviction is persisted");
        var evicted = (await ListAsync<UserSession>()).Single(session => session.Id == prior.Id);
        evicted.RevokedAt.ShouldNotBeNull();
        evicted.Version.ShouldBeGreaterThan(
            prior.Version + 1,
            "the eviction must advance the token past the competing selection; writing the version this request " +
            "loaded would roll the token back and let a later stale update believe it still won");
    }

    [Test]
    public async Task Selecting_a_tenant_revalidates_the_membership_after_losing_a_concurrent_session_write()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://session-race-select-suspended.localhost";
        const string email = "race-select-suspended@example.test";
        await SeedPermissionCatalogAsync();
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var requested = await SeedActiveMembershipAsync(identityId, Permissions.MembersInvite);
        var competing = await SeedActiveMembershipAsync(identityId, Permissions.RolesManage);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionSelection(SessionWriteStage.TenantSelection, competing, suspendMembershipOf: requested);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = requested.Value }, antiforgery);

        var response = await client.SendAsync(request);

        TestApp.HasPendingConcurrentSessionSelection.ShouldBeFalse("the competing write must fire while this selection is persisted");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.Code.ShouldBe("permission_denied");
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldBeNull();
        session.ActiveTenantId.ShouldBe(competing, "only the committed competing selection survives");
    }
}
