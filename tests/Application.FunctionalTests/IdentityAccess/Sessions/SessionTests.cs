using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Sessions;

public sealed class SessionTests : TestBase
{
    [Test]
    public async Task Sign_in_with_unknown_credentials_returns_the_neutral_bodyless_response_and_audits_the_failure()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var antiforgery = await GetAntiforgeryAsync(client, "https://session-login.localhost");
        using var request = JsonRequest(HttpMethod.Post, "https://session-login.localhost/api/identity/sessions", new { email = "missing@example.test", password = "not-a-secret" }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await CountAsync<UserSession>()).ShouldBe(0);
        var failedAudit = (await ListAsync<AuditEvent>()).Single(item => item.EventType == "signin.failed");
        failedAudit.ActorId.ShouldBeNull();
        failedAudit.SessionId.ShouldBeNull();
        failedAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        failedAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "signin.failed", ["outcome"] = "invalid_credentials" });
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Unconfirmed_or_locked_accounts_receive_the_neutral_sign_in_response_without_a_session(bool lockedOut)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-inactive-{lockedOut}.localhost";
        var identityId = await SeedConfirmedUserAsync($"inactive-{lockedOut}@example.test", "Testing1234!");
        await SetAccountStateAsync(identityId, emailConfirmed: lockedOut, lockedOut: lockedOut);
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email = $"inactive-{lockedOut}@example.test", password = "Testing1234!" }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await CountAsync<UserSession>()).ShouldBe(0);
        var failedAudit = (await ListAsync<AuditEvent>()).Single(item => item.EventType == "signin.failed");
        failedAudit.ActorId.ShouldBeNull();
        failedAudit.SessionId.ShouldBeNull();
        failedAudit.Metadata.Values.ShouldNotContain(value => value.Contains("Testing1234!", StringComparison.Ordinal));
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Failed_session_creation_rolls_back_all_effects_and_a_retry_creates_one_session_audit_set()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-create-rollback.localhost";
        await SeedConfirmedUserAsync("create-rollback@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.ForceUnexpectedFailure();
        using (var failedRequest = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email = "create-rollback@example.test", password = "Testing1234!" }, antiforgery))
        {
            var failed = await client.SendAsync(failedRequest);
            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            (await failed.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("internal_server_error");
            failed.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
        }

        (await CountAsync<UserSession>()).ShouldBe(0);
        (await ListAsync<AuditEvent>()).ShouldBeEmpty();
        var retryToken = await GetAntiforgeryAsync(client, host);
        using (var retryRequest = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email = "create-rollback@example.test", password = "Testing1234!" }, retryToken))
        {
            (await client.SendAsync(retryRequest)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await CountAsync<UserSession>()).ShouldBe(1);
        var audits = await ListAsync<AuditEvent>();
        audits.Count(item => item.EventType == "signin.succeeded").ShouldBe(1);
        audits.Count(item => item.EventType == "session.created").ShouldBe(1);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Sign_in_selects_an_active_tenant_only_when_exactly_one_active_membership_exists(int activeMembershipCount)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-membership-default-{activeMembershipCount}.localhost";
        var identityId = await SeedConfirmedUserAsync($"membership-default-{activeMembershipCount}@example.test", "Testing1234!");
        TenantId? onlyTenant = null;
        for (var index = 0; index < activeMembershipCount; index++)
        {
            var tenantId = await SeedActiveMembershipAsync(identityId);
            if (index == 0) onlyTenant = tenantId;
        }

        await SignInAsync(client, host, $"membership-default-{activeMembershipCount}@example.test", "Testing1234!");

        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(activeMembershipCount == 1 ? onlyTenant : null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Sign_in_rejects_missing_origin_or_antiforgery_without_a_session_or_audit(bool omitOrigin)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-csrf-{omitOrigin}.localhost";
        await SeedConfirmedUserAsync($"csrf-{omitOrigin}@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/sessions")
        {
            Content = JsonContent.Create(new { email = $"csrf-{omitOrigin}@example.test", password = "Testing1234!" })
        };
        if (!omitOrigin)
        {
            request.Headers.Add("Origin", host);
        }
        else
        {
            request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        }

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("antiforgery_validation_failed");
        (await CountAsync<UserSession>()).ShouldBe(0);
        (await ListAsync<AuditEvent>()).ShouldBeEmpty();
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Confirmed_user_sign_in_persists_a_session_audits_without_secrets_and_sets_the_host_cookie()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-success.localhost";
        await SeedConfirmedUserAsync("success@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email = "success@example.test", password = "Testing1234!" }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Set-Cookie").ShouldContain(value => value.Contains("__Host-ia-auth=", StringComparison.Ordinal) && value.Contains("secure", StringComparison.OrdinalIgnoreCase) && value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && value.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase) && !value.Contains("domain=", StringComparison.OrdinalIgnoreCase) && value.Contains("path=/", StringComparison.OrdinalIgnoreCase) && !value.Contains("expires=", StringComparison.OrdinalIgnoreCase) && !value.Contains("max-age=", StringComparison.OrdinalIgnoreCase) && !value.Contains("success@example.test", StringComparison.OrdinalIgnoreCase));
        response.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal) && value.Contains("expires=", StringComparison.OrdinalIgnoreCase) && value.Contains("path=/", StringComparison.OrdinalIgnoreCase) && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
        (await CountAsync<UserSession>()).ShouldBe(1);
        var audits = await ListAsync<AuditEvent>();
        audits.Count(item => item.EventType == "signin.succeeded").ShouldBe(1);
        audits.Count(item => item.EventType == "session.created").ShouldBe(1);
        var session = await GetOnlySessionAsync();
        var signInAudit = audits.Single(item => item.EventType == "signin.succeeded");
        var createdAudit = audits.Single(item => item.EventType == "session.created");
        signInAudit.ActorId.ShouldBe(session.IdentityId);
        signInAudit.SessionId.ShouldBe(session.Id.Value);
        createdAudit.ActorId.ShouldBe(session.IdentityId);
        createdAudit.SessionId.ShouldBe(session.Id.Value);
        signInAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        signInAudit.CorrelationId.ShouldMatch("^[0-9a-f]{32}$");
        createdAudit.CorrelationId.ShouldBe(signInAudit.CorrelationId);
        signInAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "signin.succeeded", ["outcome"] = "authenticated" });
        createdAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "session.created", ["outcome"] = "created" });
        audits.ShouldNotContain(item => item.Metadata.Values.Any(value => value.Contains("Testing1234!", StringComparison.Ordinal)));
    }

    [Test]
    public async Task Validated_session_is_the_only_optional_registration_authority_and_sign_in_rotates_antiforgery()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-registration-trust.localhost";
        var email = "session-registration@example.test";
        await SeedConfirmedUserAsync(email, "Testing1234!");
        var beforeSignIn = await GetAntiforgeryAsync(client, host);
        using (var signIn = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = "Testing1234!" }, beforeSignIn))
        {
            (await client.SendAsync(signIn)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var oldPair = JsonRequest(HttpMethod.Post, $"{host}/api/identity/organizations/register", new { email, password = "Testing1234!", legalName = "Trusted Registration", cuit = "30-12345678-9" }, beforeSignIn))
        {
            var response = await client.SendAsync(oldPair);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("antiforgery_validation_failed");
        }

        var afterSignIn = await GetAntiforgeryAsync(client, host);
        using (var matchingRegistration = JsonRequest(HttpMethod.Post, $"{host}/api/identity/organizations/register", new { email, password = "Testing1234!", legalName = "Trusted Registration", cuit = "30-12345678-9" }, afterSignIn))
        {
            (await client.SendAsync(matchingRegistration)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await CountAsync<TenantMembership>()).ShouldBe(1);

        var secondToken = await GetAntiforgeryAsync(client, host);
        using var mismatchedRegistration = JsonRequest(HttpMethod.Post, $"{host}/api/identity/organizations/register", new { email = "other@example.test", password = "Testing1234!", legalName = "Mismatched Registration", cuit = "30-87654321-0" }, secondToken);
        var mismatch = await client.SendAsync(mismatchedRegistration);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_registration");
        (await CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await CountAsync<TenantMembership>()).ShouldBe(1);
    }

    [Test]
    public async Task Repeated_sign_in_rotates_the_persisted_session_and_rejects_the_previous_cookie()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-repeat-rotation.localhost";
        await SeedConfirmedUserAsync("repeat@example.test", "Testing1234!");
        var first = await SignInAsync(client, host, "repeat@example.test", "Testing1234!");
        var second = await SignInAsync(client, host, "repeat@example.test", "Testing1234!");

        UnprotectTicket(harness, first).Principal.FindFirstValue(ClaimTypes.Sid).ShouldNotBe(UnprotectTicket(harness, second).Principal.FindFirstValue(ClaimTypes.Sid));
        var previousClient = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        using (previousClient)
        {
            previousClient.DefaultRequestHeaders.Add("Cookie", first);
            var previousContext = await previousClient.GetAsync($"{host}/api/identity/context");
            previousContext.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(1);
        var sessions = await ListAsync<UserSession>();
        var superseded = sessions.Single(session => session.RevokedAt is not null);
        var survivor = sessions.Single(session => session.RevokedAt is null);
        var audits = await ListAsync<AuditEvent>();
        var supersededAudit = audits.Single(item => item.EventType == "session.revoked");
        supersededAudit.ActorId.ShouldBe(superseded.IdentityId);
        supersededAudit.SessionId.ShouldBe(superseded.Id.Value);
        supersededAudit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "session.revoked", ["outcome"] = "superseded" });
        supersededAudit.CorrelationId.ShouldBe(audits.Single(item => item.EventType == "signin.succeeded" && item.SessionId == survivor.Id.Value).CorrelationId);
        (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase(SessionFailure.Revoked)]
    [TestCase(SessionFailure.IdleExpired)]
    [TestCase(SessionFailure.AbsoluteExpired)]
    public async Task Invalidated_optional_session_cannot_register_an_organization(SessionFailure failure)
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = $"https://session-registration-{failure}.localhost";
        var email = $"{failure}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        await ApplyFailureStateAsync(identityId, failure, clock);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/organizations/register", new { email, password = "Testing1234!", legalName = "Invalidated Session", cuit = "30-12345678-9" }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        (await CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await CountAsync<TenantMembership>()).ShouldBe(0);
    }

    [Test]
    public async Task Revoke_current_session_with_a_valid_antiforgery_pair_returns_bodyless_no_content()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-revoke.localhost";
        await SeedConfirmedUserAsync("revoke@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "revoke@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Delete, "https://session-revoke.localhost/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        response.Headers.GetValues("Set-Cookie").ShouldContain(value =>
            value.Contains("__Host-ia-auth=", StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("path=/", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        response.Headers.GetValues("Set-Cookie").ShouldContain(value =>
            value.Contains("__Host-XSRF-TOKEN=", StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("path=/", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        var session = await GetOnlySessionAsync();
        session.RevokedAt.ShouldNotBeNull();
        var revokedAudit = (await ListAsync<AuditEvent>()).Single(item => item.EventType == "session.revoked");
        revokedAudit.ActorId.ShouldBe(session.IdentityId);
        revokedAudit.SessionId.ShouldBe(session.Id.Value);
        revokedAudit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        revokedAudit.Metadata.Values.ShouldNotContain(value => value.Contains("Testing1234!", StringComparison.Ordinal) || value.Contains(authCookie, StringComparison.Ordinal));
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(1);

        using var replay = JsonRequest(HttpMethod.Delete, "https://session-revoke.localhost/api/identity/sessions/current", null, antiforgery);
        var replayResponse = await client.SendAsync(replay);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(1);
    }

    [Test]
    public async Task Revoked_authentication_cookie_and_its_prior_antiforgery_pair_cannot_authorize_registration()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-signout-rotation.localhost";
        var email = "signout-rotation@example.test";
        await SeedConfirmedUserAsync(email, "Testing1234!");
        var preSignIn = await GetAntiforgeryPairAsync(client, host);
        using var signIn = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = "Testing1234!" }, preSignIn.RequestToken);
        var signInResponse = await client.SendAsync(signIn);
        signInResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var oldAuthCookie = signInResponse.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
        var oldAntiforgeryCookie = preSignIn.Cookie;
        var oldRequestToken = preSignIn.RequestToken;
        var revokeToken = await GetAntiforgeryAsync(client, host);
        using (var revoke = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, revokeToken))
        {
            (await client.SendAsync(revoke)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var staleClient = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        staleClient.DefaultRequestHeaders.Add("Cookie", $"{oldAuthCookie}; {oldAntiforgeryCookie}");
        using var staleRegistration = JsonRequest(HttpMethod.Post, $"{host}/api/identity/organizations/register", new { email, password = "Testing1234!", legalName = "Revoked Registration", cuit = "30-12345678-9" }, oldRequestToken);
        var response = await staleClient.SendAsync(staleRegistration);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        (await CountAsync<OrganizationProfile>()).ShouldBe(0);
    }

    [Test]
    public async Task Revoke_current_session_rolls_back_without_deleting_the_cookie_when_persistence_fails()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-revoke-rollback.localhost";
        await SeedConfirmedUserAsync("revoke-rollback@example.test", "Testing1234!");
        await SignInAsync(client, host, "revoke-rollback@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.ForceSessionRevokePersistenceFailure();
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await GetOnlySessionAsync()).RevokedAt.ShouldBeNull();
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Revoke_current_session_rejects_an_idle_expired_cookie_without_writing_a_revocation_audit()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-revoke-expired.localhost";
        await SeedConfirmedUserAsync("revoke-expired@example.test", "Testing1234!");
        await SignInAsync(client, host, "revoke-expired@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        clock.Advance(TimeSpan.FromMinutes(31));
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetOnlySessionAsync()).RevokedAt.ShouldBeNull();
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
    }

    [Test]
    public async Task Current_context_returns_the_identity_context_response()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-context.localhost";
        var identityId = await SeedConfirmedUserAsync("context@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "context@example.test", "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await GetOnlySessionAsync();
        var body = await ReadJsonAsync(response);
        body.EnumerateObject().Select(property => property.Name).Order().ShouldBe(new[] { "activeTenant", "availableTenants", "permissions", "session", "user" });
        body.GetProperty("user").GetProperty("id").GetString().ShouldBe(identityId.ToString("N"));
        body.GetProperty("user").GetProperty("displayName").GetString().ShouldBe("context@example.test");
        body.GetProperty("user").GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
        body.GetProperty("activeTenant").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("availableTenants").GetArrayLength().ShouldBe(0);
        body.GetProperty("permissions").GetArrayLength().ShouldBe(0);
        body.GetProperty("session").GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(session.AbsoluteExpiresAt);
        body.GetProperty("session").GetProperty("requiresTwoFactor").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Current_context_refreshes_idle_expiry_without_moving_absolute_expiry()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-refresh.localhost";
        await SeedConfirmedUserAsync("refresh@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "refresh@example.test", "Testing1234!");
        var before = await GetOnlySessionAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await GetOnlySessionAsync();
        after.LastSeenAt.ShouldBe(clock.GetUtcNow());
        after.IdleExpiresAt.ShouldBe(before.IdleExpiresAt.AddMinutes(10));
        after.AbsoluteExpiresAt.ShouldBe(before.AbsoluteExpiresAt);
    }

    [Test]
    public async Task Current_context_rejects_when_refresh_loses_a_concurrent_revocation()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-refresh-revoked.localhost";
        await SeedConfirmedUserAsync("refresh-revoked@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "refresh-revoked@example.test", "Testing1234!");
        var before = await GetOnlySessionAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        TestApp.EnableSessionValidationConcurrentRevoke();
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldNotBeNull();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.AbsoluteExpiresAt.ShouldBe(before.AbsoluteExpiresAt);
    }

    [TestCase(SessionFailure.Revoked)]
    [TestCase(SessionFailure.IdleExpired)]
    [TestCase(SessionFailure.AbsoluteExpired)]
    [TestCase(SessionFailure.Unconfirmed)]
    [TestCase(SessionFailure.LockedOut)]
    public async Task Current_context_rejects_invalid_persisted_session_or_identity_state(SessionFailure failure)
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 2, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = $"https://session-{failure.ToString().ToLowerInvariant()}.localhost";
        var userId = await SeedConfirmedUserAsync($"{failure.ToString().ToLowerInvariant()}@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, $"{failure.ToString().ToLowerInvariant()}@example.test", "Testing1234!");
        await ApplyFailureStateAsync(userId, failure, clock);
        var before = await GetOnlySessionAsync();
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var payload = await response.Content.ReadAsStringAsync();
        payload.ShouldNotContain("Testing1234!", Case.Insensitive);
        System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe("invalid_session");
        var after = await GetOnlySessionAsync();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.IdleExpiresAt.ShouldBe(before.IdleExpiresAt);
        after.Version.ShouldBe(before.Version);
    }

    [Test]
    public async Task Current_context_uses_only_the_validated_cookie_identity_and_session_claims()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-cookie-claims.localhost";
        var identityId = await SeedConfirmedUserAsync("claims@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "claims@example.test", "Testing1234!");
        var ticket = UnprotectTicket(harness, authCookie);
        ticket.Principal.Claims.Select(claim => claim.Type).Order().ShouldBe(new[] { ClaimTypes.NameIdentifier, ClaimTypes.Sid }.Order());
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        client.DefaultRequestHeaders.Add("X-Identity-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("user").GetProperty("id").GetString().ShouldBe(identityId.ToString("N"));
        body.GetProperty("activeTenant").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBeNull();
    }

    [TestCase(CookieReferenceFailure.DeletedSession)]
    [TestCase(CookieReferenceFailure.DeletedUser)]
    [TestCase(CookieReferenceFailure.MismatchedIdentityAndSession)]
    public async Task Current_context_rejects_missing_or_mismatched_cookie_references(CookieReferenceFailure failure)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-cookie-{failure.ToString().ToLowerInvariant()}.localhost";
        var primaryUserId = await SeedConfirmedUserAsync($"{failure.ToString().ToLowerInvariant()}@example.test", "Testing1234!");
        await SignInAsync(client, host, $"{failure.ToString().ToLowerInvariant()}@example.test", "Testing1234!");
        var cookie = failure switch
        {
            CookieReferenceFailure.DeletedSession => ProtectTicket(harness, primaryUserId, Guid.NewGuid()),
            CookieReferenceFailure.DeletedUser => ProtectTicket(harness, Guid.NewGuid(), Guid.NewGuid()),
            CookieReferenceFailure.MismatchedIdentityAndSession => ProtectTicket(harness, primaryUserId, (await CreateDetachedSessionAsync(await SeedConfirmedUserAsync("mismatch-peer@example.test", "Testing1234!"))).Id.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var before = (await ListAsync<UserSession>()).Single(session => session.IdentityId == primaryUserId);
        using var requestClient = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        requestClient.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await requestClient.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        var after = (await ListAsync<UserSession>()).Single(session => session.IdentityId == primaryUserId);
        after.RevokedAt.ShouldBeNull();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.Version.ShouldBe(before.Version);
    }

    [Test]
    public async Task Selecting_a_tenant_with_a_valid_antiforgery_pair_returns_the_updated_context()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-tenant.localhost";
        var userId = await SeedConfirmedUserAsync("tenant@example.test", "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        await SeedActiveMembershipAsync(userId);
        await SignInAsync(client, host, "tenant@example.test", "Testing1234!");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBeNull();
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = tenantId.Value }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("user").GetProperty("id").GetString().ShouldBe(userId.ToString("N"));
        body.GetProperty("activeTenant").GetProperty("id").GetGuid().ShouldBe(tenantId.Value);
        body.GetProperty("activeTenant").GetProperty("type").GetString().ShouldBe("Organization");
        body.GetProperty("activeTenant").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("availableTenants").EnumerateArray().Select(tenant => tenant.GetProperty("id").GetGuid()).ShouldContain(tenantId.Value);
        body.GetProperty("availableTenants").GetArrayLength().ShouldBe(2);
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(tenantId);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Current_context_clears_a_selection_when_its_membership_or_tenant_becomes_inactive(bool suspendTenant)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-context-inactive-{(suspendTenant ? "tenant" : "membership")}.localhost";
        var email = $"inactive-{(suspendTenant ? "tenant" : "membership")}@example.test";
        var userId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(tenantId);
        if (suspendTenant) await SuspendTenantAsync(tenantId); else await SuspendMembershipAsync(tenantId);
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBeNull();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Selecting_an_unowned_or_empty_tenant_is_safe_and_does_not_mutate_the_session(bool emptyTenant)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-tenant-denied-{emptyTenant}.localhost";
        var userId = await SeedConfirmedUserAsync($"tenant-denied-{emptyTenant}@example.test", "Testing1234!");
        await SeedActiveMembershipAsync(userId);
        var otherUser = await SeedConfirmedUserAsync($"tenant-other-{emptyTenant}@example.test", "Testing1234!");
        var otherTenant = await SeedActiveMembershipAsync(otherUser);
        await SignInAsync(client, host, $"tenant-denied-{emptyTenant}@example.test", "Testing1234!");
        var before = await GetOnlySessionAsync();
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = emptyTenant ? Guid.Empty : otherTenant.Value }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(emptyTenant ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>();
        problem!.Code.ShouldBe(emptyTenant ? "invalid_request" : "permission_denied");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(before.ActiveTenantId);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Selecting_a_nonexistent_tenant_is_enumeration_safe()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-tenant-missing.localhost";
        var userId = await SeedConfirmedUserAsync("tenant-missing@example.test", "Testing1234!");
        await SeedActiveMembershipAsync(userId);
        await SignInAsync(client, host, "tenant-missing@example.test", "Testing1234!");
        var before = await GetOnlySessionAsync();
        var anti = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = Guid.NewGuid() }, anti);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("permission_denied");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(before.ActiveTenantId);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Selecting_an_inactive_membership_or_suspended_tenant_is_enumeration_safe(bool suspendTenant)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-tenant-inactive-{(suspendTenant ? "tenant" : "membership")}.localhost";
        var email = $"tenant-inactive-{(suspendTenant ? "tenant" : "membership")}@example.test";
        var userId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        if (suspendTenant) await SuspendTenantAsync(tenantId); else await SuspendMembershipAsync(tenantId);
        await SignInAsync(client, host, email, "Testing1234!");
        var before = await GetOnlySessionAsync();
        var anti = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = tenantId.Value }, anti);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("permission_denied");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(before.ActiveTenantId);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Selecting_with_an_expired_session_returns_invalid_session_without_mutation_or_cookie()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-tenant-expired.localhost";
        var userId = await SeedConfirmedUserAsync("tenant-expired@example.test", "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        await SignInAsync(client, host, "tenant-expired@example.test", "Testing1234!");
        var before = await GetOnlySessionAsync();
        var anti = await GetAntiforgeryAsync(client, host);
        clock.Advance(TimeSpan.FromMinutes(31));
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = tenantId.Value }, anti);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(before.ActiveTenantId);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [TestCase(0)]
    [TestCase(-5)]
    public async Task Current_context_rejects_a_concurrent_revocation_even_when_refresh_does_not_move_last_seen(int minutesRelativeToLastSeen)
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 3, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var suffix = minutesRelativeToLastSeen == 0 ? "equal" : "earlier";
        var host = $"https://session-refresh-noop-revoked-{suffix}.localhost";
        var email = $"refresh-noop-{suffix}@example.test";
        await SeedConfirmedUserAsync(email, "Testing1234!");
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        clock.Advance(TimeSpan.FromMinutes(10));
        (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await GetOnlySessionAsync();
        before.LastSeenAt.ShouldBe(clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(minutesRelativeToLastSeen));
        TestApp.EnableSessionValidationConcurrentRevoke();

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldNotBeNull();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.IdleExpiresAt.ShouldBe(before.IdleExpiresAt);
        after.AbsoluteExpiresAt.ShouldBe(before.AbsoluteExpiresAt);
        after.Version.ShouldBe(before.Version + 1);
    }

    [TestCase(EmptyClaim.Sid)]
    [TestCase(EmptyClaim.NameIdentifier)]
    [TestCase(EmptyClaim.Both)]
    public async Task Protected_ticket_with_an_empty_identifier_fails_closed_with_401_instead_of_500(EmptyClaim emptyClaim)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-empty-{emptyClaim.ToString().ToLowerInvariant()}.localhost";
        var email = $"empty-{emptyClaim.ToString().ToLowerInvariant()}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        await SignInAsync(client, host, email, "Testing1234!");
        var before = await GetOnlySessionAsync();
        var cookie = ProtectTicket(
            harness,
            emptyClaim is EmptyClaim.NameIdentifier or EmptyClaim.Both ? Guid.Empty : identityId,
            emptyClaim is EmptyClaim.Sid or EmptyClaim.Both ? Guid.Empty : before.Id.Value);
        using var requestClient = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        requestClient.DefaultRequestHeaders.Add("Cookie", cookie);

        var context = await requestClient.GetAsync($"{host}/api/identity/context");
        var antiforgery = await requestClient.GetAsync($"{host}/api/identity/antiforgery");

        context.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await context.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        antiforgery.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldBeNull();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.Version.ShouldBe(before.Version);
    }

    [Test]
    public async Task Current_context_accepts_a_valid_session_when_a_parallel_request_touched_it_first()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 4, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-parallel-touch.localhost";
        var identityId = await SeedConfirmedUserAsync("parallel-touch@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "parallel-touch@example.test", "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var before = await GetOnlySessionAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        TestApp.EnableConcurrentSessionTouch(SessionWriteStage.Validation);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        TestApp.HasPendingConcurrentSessionTouch.ShouldBeFalse("the competing touch must fire while the request validates its session");
        (await ReadJsonAsync(response)).GetProperty("user").GetProperty("id").GetString().ShouldBe(identityId.ToString("N"));
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldBeNull();
        after.LastSeenAt.ShouldBeGreaterThan(before.LastSeenAt);
        after.Version.ShouldBe(before.Version);
    }

    [Test]
    public async Task Revoke_current_session_succeeds_when_a_parallel_request_touched_the_session_first()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-revoke-parallel-touch.localhost";
        await SeedConfirmedUserAsync("revoke-parallel@example.test", "Testing1234!");
        await SignInAsync(client, host, "revoke-parallel@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionTouch(SessionWriteStage.Revocation);
        using var request = JsonRequest(HttpMethod.Delete, $"{host}/api/identity/sessions/current", null, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        TestApp.HasPendingConcurrentSessionTouch.ShouldBeFalse("the competing touch must fire while the revocation is persisted");
        (await GetOnlySessionAsync()).RevokedAt.ShouldNotBeNull();
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(1);
        (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Selecting_a_tenant_succeeds_when_a_parallel_request_touched_the_session_first()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-tenant-parallel-touch.localhost";
        var userId = await SeedConfirmedUserAsync("tenant-parallel@example.test", "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        await SeedActiveMembershipAsync(userId);
        await SignInAsync(client, host, "tenant-parallel@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentSessionTouch(SessionWriteStage.TenantSelection);
        using var request = JsonRequest(HttpMethod.Put, $"{host}/api/identity/context/tenant", new { tenantId = tenantId.Value }, antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        TestApp.HasPendingConcurrentSessionTouch.ShouldBeFalse("the competing touch must fire while the selection is persisted");
        (await ReadJsonAsync(response)).GetProperty("activeTenant").GetProperty("id").GetGuid().ShouldBe(tenantId.Value);
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(tenantId);
    }

    [TestCase("GET", "/api/identity/context")]
    [TestCase("PUT", "/api/identity/context/tenant")]
    [TestCase("DELETE", "/api/identity/sessions/current")]
    public async Task Protected_identity_routes_without_a_cookie_return_authentication_required(string method, string path)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://session-anonymous-{method.ToLowerInvariant()}.localhost";
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{host}{path}");
        if (method == "PUT") request.Content = JsonContent.Create(new { tenantId = Guid.NewGuid() });

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("authentication_required");
    }

    [TestCase("{")]
    [TestCase("{\"email\": 123, \"password\": []}")]
    public async Task Sign_in_with_a_malformed_body_is_a_safe_400_invalid_request_without_session_or_audit(string malformedJson)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-malformed.localhost";
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/sessions")
        {
            Content = new StringContent(malformedJson, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_request");
        (await CountAsync<UserSession>()).ShouldBe(0);
        (await ListAsync<AuditEvent>()).ShouldBeEmpty();
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    [TestCase("DELETE", "/api/identity/sessions/current", true)]
    [TestCase("DELETE", "/api/identity/sessions/current", false)]
    [TestCase("PUT", "/api/identity/context/tenant", true)]
    [TestCase("PUT", "/api/identity/context/tenant", false)]
    public async Task Authenticated_mutations_reject_missing_origin_or_antiforgery_without_state_change(string method, string path, bool omitOrigin)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var slug = $"{method}-{omitOrigin}".ToLowerInvariant();
        var host = $"https://session-csrf-auth-{slug}.localhost";
        var email = $"csrf-auth-{slug}@example.test";
        var userId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        await SeedActiveMembershipAsync(userId);
        await SignInAsync(client, host, email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        var before = await GetOnlySessionAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{host}{path}");
        if (method == "PUT") request.Content = JsonContent.Create(new { tenantId = tenantId.Value });
        if (omitOrigin) request.Headers.Add("X-CSRF-TOKEN", antiforgery); else request.Headers.Add("Origin", host);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("antiforgery_validation_failed");
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldBeNull();
        after.ActiveTenantId.ShouldBe(before.ActiveTenantId);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
    }

    private static async Task<System.Text.Json.JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    public async Task Continuous_activity_never_extends_a_session_past_its_absolute_lifetime()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 5, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-absolute-cap.localhost";
        await SeedConfirmedUserAsync("absolute-cap@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "absolute-cap@example.test", "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var created = await GetOnlySessionAsync();

        for (var elapsed = TimeSpan.FromMinutes(25); elapsed < TimeSpan.FromHours(12); elapsed += TimeSpan.FromMinutes(25))
        {
            clock.Advance(TimeSpan.FromMinutes(25));
            (await client.GetAsync($"{host}/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK, $"activity at {elapsed} keeps the session alive");
        }

        var lastActive = await GetOnlySessionAsync();
        lastActive.IdleExpiresAt.ShouldBe(created.AbsoluteExpiresAt);
        lastActive.AbsoluteExpiresAt.ShouldBe(created.AbsoluteExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(25));

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        (await GetOnlySessionAsync()).LastSeenAt.ShouldBe(lastActive.LastSeenAt);
    }

    [Test]
    public async Task Sign_in_supersedes_only_live_sessions_and_leaves_expired_ones_untouched()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 1, 6, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        var host = "https://session-supersede-live.localhost";
        await SeedConfirmedUserAsync("supersede-live@example.test", "Testing1234!");
        await SignInAsync(client, host, "supersede-live@example.test", "Testing1234!");
        var expired = await GetOnlySessionAsync();
        clock.Advance(TimeSpan.FromMinutes(31));

        await SignInAsync(client, host, "supersede-live@example.test", "Testing1234!");

        var sessions = await ListAsync<UserSession>();
        sessions.Single(session => session.Id == expired.Id).RevokedAt.ShouldBeNull("an idle-expired session is already dead and is not superseded");
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
        var live = sessions.Single(session => session.Id != expired.Id);
        live.RevokedAt.ShouldBeNull();

        await SignInAsync(client, host, "supersede-live@example.test", "Testing1234!");

        (await ListAsync<UserSession>()).Single(session => session.Id == live.Id).RevokedAt.ShouldNotBeNull();
        var audits = await ListAsync<AuditEvent>();
        audits.Count(item => item.EventType == "session.revoked").ShouldBe(1);
        audits.Single(item => item.EventType == "session.revoked").SessionId.ShouldBe(live.Id.Value);
    }

    [Test]
    public async Task Sign_in_presenting_a_cookie_revoked_during_validation_succeeds_without_resurrecting_that_session()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-signin-stale-cookie.localhost";
        await SeedConfirmedUserAsync("stale-cookie@example.test", "Testing1234!");
        var authCookie = await SignInAsync(client, host, "stale-cookie@example.test", "Testing1234!");
        var presented = await GetOnlySessionAsync();
        using var anonymous = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        var pair = await GetAntiforgeryPairAsync(anonymous, host);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email = "stale-cookie@example.test", password = "Testing1234!" }, pair.RequestToken);
        request.Headers.Add("Cookie", $"{authCookie}; {pair.Cookie}");
        TestApp.EnableSessionValidationConcurrentRevoke();

        var response = await anonymous.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var sessions = await ListAsync<UserSession>();
        sessions.Count.ShouldBe(2);
        var stale = sessions.Single(session => session.Id == presented.Id);
        stale.RevokedAt.ShouldNotBeNull();
        stale.LastSeenAt.ShouldBe(presented.LastSeenAt);
        stale.IdleExpiresAt.ShouldBe(presented.IdleExpiresAt);
        sessions.Single(session => session.Id != presented.Id).RevokedAt.ShouldBeNull();
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "session.revoked").ShouldBe(0);
    }

    [Test]
    public async Task Current_context_tolerates_a_parallel_clearing_of_an_inactive_tenant_selection()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-parallel-clear.localhost";
        var userId = await SeedConfirmedUserAsync("parallel-clear@example.test", "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        var authCookie = await SignInAsync(client, host, "parallel-clear@example.test", "Testing1234!");
        (await GetOnlySessionAsync()).ActiveTenantId.ShouldBe(tenantId);
        await SuspendMembershipAsync(tenantId);
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        TestApp.EnableConcurrentSessionClear();

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        TestApp.HasPendingConcurrentSessionClear.ShouldBeFalse("the competing clearing must fire while the selection is cleared");
        (await ReadJsonAsync(response)).GetProperty("activeTenant").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        var after = await GetOnlySessionAsync();
        after.ActiveTenantId.ShouldBeNull();
        after.RevokedAt.ShouldBeNull();
    }

    [Test]
    public async Task Current_context_rejects_when_a_parallel_revocation_wins_the_inactive_tenant_clearing()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = "https://session-parallel-clear-revoked.localhost";
        var userId = await SeedConfirmedUserAsync("parallel-clear-revoked@example.test", "Testing1234!");
        var tenantId = await SeedActiveMembershipAsync(userId);
        var authCookie = await SignInAsync(client, host, "parallel-clear-revoked@example.test", "Testing1234!");
        await SuspendMembershipAsync(tenantId);
        var before = await GetOnlySessionAsync();
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        TestApp.EnableConcurrentSessionRevoke(SessionWriteStage.TenantClearing);

        var response = await client.GetAsync($"{host}/api/identity/context");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        TestApp.HasPendingConcurrentSessionRevoke.ShouldBeFalse("the competing revocation must fire while the selection is cleared");
        (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Infrastructure.ApiProblemDetails>())!.Code.ShouldBe("invalid_session");
        var after = await GetOnlySessionAsync();
        after.RevokedAt.ShouldNotBeNull();
        after.ActiveTenantId.ShouldBeNull();
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
    }

    [Test]
    [Category("LoginControls")]
    public async Task Client_address_partition_allows_20_sign_in_attempts_per_5_minutes_then_rejects_with_429_problem_details()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 5, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        const string host = "https://login-ip-limit.localhost";
        const string attacker = "203.0.113.20";
        var antiforgery = await GetAntiforgeryAsync(client, host);

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            using var request = LoginRequest(host, $"unknown-{attempt}@example.test", "not-a-secret", antiforgery, attacker);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} must stay the generic credential response");
            (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }

        using (var exhausted = LoginRequest(host, "unknown-21@example.test", "not-a-secret", antiforgery, attacker))
        {
            await AssertRateLimitedAsync(await client.SendAsync(exhausted), maxRetryAfterSeconds: 300, "unknown-21", attacker);
        }

        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(20, "a rejected request never reaches the endpoint");
        (await CountAsync<UserSession>()).ShouldBe(0);
        using (var otherClient = LoginRequest(host, "unknown-22@example.test", "not-a-secret", antiforgery, "203.0.113.21"))
        {
            (await client.SendAsync(otherClient)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "another client address has its own partition");
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        using (var recovered = LoginRequest(host, "unknown-23@example.test", "not-a-secret", antiforgery, attacker))
        {
            (await client.SendAsync(recovered)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "the window elapses by the injected clock");
        }

        using var bootstrap = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        bootstrap.Headers.Add("X-Forwarded-For", attacker);
        (await client.SendAsync(bootstrap)).StatusCode.ShouldBe(HttpStatusCode.OK, "the antiforgery bootstrap is never rate limited");
    }

    [Test]
    [Category("LoginControls")]
    public async Task Account_partition_allows_10_attempts_per_15_minutes_across_every_spelling_of_one_account_then_rejects_with_429()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 5, 2, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        const string host = "https://login-account-limit.localhost";
        var spellings = new[] { "target@example.test", "TARGET@example.test", " target@Example.Test ", "Target@EXAMPLE.TEST", "\ttarget@example.test\n" };
        var antiforgery = await GetAntiforgeryAsync(client, host);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var request = LoginRequest(host, spellings[attempt % spellings.Length], "not-a-secret", antiforgery, $"203.0.113.{30 + attempt}");
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} must stay the generic credential response");
        }

        using (var exhausted = LoginRequest(host, "Target@Example.Test", "not-a-secret", antiforgery, "203.0.113.41"))
        {
            await AssertRateLimitedAsync(await client.SendAsync(exhausted), maxRetryAfterSeconds: 900, "target@example.test", "203.0.113.41");
        }

        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(10, "a rejected request never reaches the endpoint");
        using (var otherAccount = LoginRequest(host, "someone-else@example.test", "not-a-secret", antiforgery, "203.0.113.42"))
        {
            (await client.SendAsync(otherAccount)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "another account has its own partition");
        }

        clock.Advance(TimeSpan.FromMinutes(15));
        using var recovered = LoginRequest(host, "target@example.test", "not-a-secret", antiforgery, "203.0.113.43");
        (await client.SendAsync(recovered)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "the window elapses by the injected clock");
    }

    [Test]
    [Category("LoginControls")]
    public async Task Five_failed_sign_ins_lock_the_account_for_15_minutes_by_the_injected_clock_even_with_the_correct_password()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        const string host = "https://login-lockout.localhost";
        const string email = "lockout@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var request = LoginRequest(host, email, "Wrong-password-1!", antiforgery, $"203.0.113.{50 + attempt}");
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"failure {attempt} must stay the generic credential response");
            response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
            if (attempt < 5)
            {
                var counting = await GetUserAsync(identityId);
                counting.AccessFailedCount.ShouldBe(attempt);
                counting.LockoutEnd.ShouldBeNull();
            }
        }

        var locked = await GetUserAsync(identityId);
        locked.LockoutEnd.ShouldBe(clock.GetUtcNow().AddMinutes(15));
        locked.AccessFailedCount.ShouldBe(0, "the lockout starts a fresh failure window");
        (await CountAsync<UserSession>()).ShouldBe(0);

        using (var lockedAttempt = LoginRequest(host, email, "Testing1234!", antiforgery, "203.0.113.56"))
        {
            var response = await client.SendAsync(lockedAttempt);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse("a locked account gets no session even with the correct password");
        }

        (await CountAsync<UserSession>()).ShouldBe(0);
        (await GetUserAsync(identityId)).LockoutEnd.ShouldBe(locked.LockoutEnd);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(6);

        clock.Advance(TimeSpan.FromMinutes(15));
        using var recovered = LoginRequest(host, email, "Testing1234!", antiforgery, "203.0.113.57");
        var recoveredResponse = await client.SendAsync(recovered);
        recoveredResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        recoveredResponse.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal));
        (await CountAsync<UserSession>()).ShouldBe(1);
        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBe(0);
    }

    [Test]
    [Category("LoginControls")]
    public async Task Successful_sign_in_resets_the_failed_access_count()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://login-failure-reset.localhost";
        const string email = "failure-reset@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = LoginRequest(host, email, "Wrong-password-1!", antiforgery, null);
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBe(2);

        await SignInAsync(client, host, email, "Testing1234!");

        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBe(0);
        (await CountAsync<UserSession>()).ShouldBe(1);
    }

    [TestCase(NeutralOutcome.UnknownAccount)]
    [TestCase(NeutralOutcome.UnconfirmedAccount)]
    [TestCase(NeutralOutcome.LockedAccount)]
    [TestCase(NeutralOutcome.WrongPassword)]
    [TestCase(NeutralOutcome.ValidCredentials)]
    [Category("LoginControls")]
    public async Task Every_sign_in_outcome_performs_exactly_one_password_verification_so_timing_does_not_reveal_account_state(NeutralOutcome outcome)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://login-timing-{outcome}.localhost".ToLowerInvariant();
        var email = $"timing-{outcome}@example.test".ToLowerInvariant();
        if (outcome != NeutralOutcome.UnknownAccount)
        {
            var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
            if (outcome == NeutralOutcome.UnconfirmedAccount) await SetAccountStateAsync(identityId, emailConfirmed: false, lockedOut: false);
            if (outcome == NeutralOutcome.LockedAccount) await SetAccountStateAsync(identityId, emailConfirmed: true, lockedOut: true);
        }

        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.ResetPasswordVerificationCount();
        using var request = LoginRequest(host, email, outcome == NeutralOutcome.WrongPassword ? "Wrong-password-1!" : "Testing1234!", antiforgery, null);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        TestApp.PasswordVerificationCount.ShouldBe(1, "every outcome must cost exactly one password verification");
        (await CountAsync<UserSession>()).ShouldBe(outcome == NeutralOutcome.ValidCredentials ? 1 : 0);
    }

    [Test]
    [Category("LoginControls")]
    public async Task Rate_limited_sign_in_flow_never_logs_the_email_the_client_address_or_the_partition_keys()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://login-log-hygiene.localhost";
        const string email = "Hygiene.Owner@Example.Test";
        const string rejectedAddress = "203.0.113.77";
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.ResetCapturedLogs();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var request = LoginRequest(host, email, "not-a-secret", antiforgery, $"203.0.113.{60 + attempt}");
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var exhausted = LoginRequest(host, email, "not-a-secret", antiforgery, rejectedAddress))
        {
            await AssertRateLimitedAsync(await client.SendAsync(exhausted), maxRetryAfterSeconds: 900, "hygiene.owner", rejectedAddress);
        }

        var logs = TestApp.CapturedLogs;
        logs.ShouldNotBeEmpty("the pipeline logs at least the hosting diagnostics, so an empty capture would prove nothing");
        var forbidden = new[]
        {
            "hygiene.owner",
            rejectedAddress,
            "203.0.113.6",
            "203.0.113.7",
            "not-a-secret",
            CleanArchitecture.Web.Infrastructure.Identity.LoginRateLimitPartitioner.AccountKey(email),
            CleanArchitecture.Web.Infrastructure.Identity.LoginRateLimitPartitioner.ClientKey(rejectedAddress)
        };
        logs.Where(entry => forbidden.Any(secret => entry.Contains(secret, StringComparison.OrdinalIgnoreCase))).ShouldBeEmpty();
    }

    [Test]
    [Category("LoginControls")]
    public async Task Account_partition_cannot_be_bypassed_with_a_non_utf8_json_charset()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://login-account-charset.localhost";
        var antiforgery = await GetAntiforgeryAsync(client, host);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var request = Utf16LoginRequest(host, "charset-target@example.test", antiforgery, $"203.0.113.{60 + attempt}");
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} is decoded exactly like the endpoint decodes it");
        }

        using var exhausted = Utf16LoginRequest(host, "Charset-Target@Example.Test", antiforgery, "203.0.113.71");
        await AssertRateLimitedAsync(await client.SendAsync(exhausted), maxRetryAfterSeconds: 900, "charset-target@example.test");
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(10, "the rejected request never reaches the endpoint");
    }

    [Test]
    [Category("LoginControls")]
    public async Task A_failed_sign_in_that_loses_a_concurrent_failure_update_still_records_its_attempt()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://login-parallel-failures.localhost";
        var identityId = await SeedConfirmedUserAsync("parallel-failures@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentFailedAccess();
        using var request = LoginRequest(host, "parallel-failures@example.test", "wrong-password", antiforgery, "203.0.113.80");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        TestApp.HasPendingConcurrentFailedAccess.ShouldBeFalse("the competing failure must fire while this failure is persisted");
        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBe(2, "both failed attempts are counted");
        (await CountAsync<UserSession>()).ShouldBe(0);
    }

    private static HttpRequestMessage Utf16LoginRequest(string host, string email, string antiforgery, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/sessions");
        var json = System.Text.Json.JsonSerializer.Serialize(new { email, password = "not-a-secret" });
        request.Content = new ByteArrayContent(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(json)).ToArray());
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-16" };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Test]
    [Category("LoginControls")]
    public async Task A_failed_sign_in_that_repeatedly_loses_the_failure_update_stays_neutral_and_never_reveals_the_account()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://login-repeated-failure-loss.localhost";
        var identityId = await SeedConfirmedUserAsync("repeated-loss@example.test", "Testing1234!");
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableConcurrentFailedAccess(times: 3);
        using var request = LoginRequest(host, "repeated-loss@example.test", "wrong-password", antiforgery, "203.0.113.90");

        var response = await client.SendAsync(request);

        // The neutral response is the whole point: an unknown account answers 204, so an existing one must too.
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
        (await CountAsync<UserSession>()).ShouldBe(0);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(1, "the attempt is still audited");
        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBeGreaterThanOrEqualTo(3, "every competing attempt was counted");
    }

    private static ProductionHarness CreateProductionHarness(TimeProvider? timeProvider = null)
    {
        var factory = new WebApiFactory(
            FunctionalTestSetup.ConnectionString,
            Microsoft.Extensions.Hosting.Environments.Production,
            useTestAuthentication: false,
            useTestIdentityAccessDoubles: false,
            timeProvider: timeProvider);
        return new ProductionHarness(factory, factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false }));
    }

    private static async Task<string> SignInAsync(HttpClient client, string host, string email, string password)
    {
        var antiforgery = await GetAntiforgeryAsync(client, host);
        using var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<string> GetAntiforgeryAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
    }

    private static async Task<AntiforgeryPair> GetAntiforgeryPairAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return new AntiforgeryPair(
            (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken,
            response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string uri, object? body, string antiforgery)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Origin", new Uri(uri).GetLeftPart(UriPartial.Authority));
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        return request;
    }

    private static async Task<Guid> SeedConfirmedUserAsync(string email, string password)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
        return user.Id;
    }

    private static async Task SetAccountStateAsync(Guid identityId, bool emailConfirmed, bool lockedOut)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(candidate => candidate.Id == identityId);
        user.EmailConfirmed = emailConfirmed;
        user.LockoutEnabled = lockedOut;
        user.LockoutEnd = lockedOut ? DateTimeOffset.UtcNow.AddMinutes(15) : null;
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }

    private static async Task<TenantId> SeedActiveMembershipAsync(Guid userId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"session-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, userId);
        membership.Activate(tenant);
        context.AddRange(tenant, membership);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<int> CountAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<TEntity>().CountAsync();
    }

    private static async Task<List<TEntity>> ListAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<TEntity>().ToListAsync();
    }

    private static async Task<UserSession> GetOnlySessionAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserSessions.SingleAsync();
    }

    private static async Task<UserSession> CreateDetachedSessionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var session = UserSession.Create(identityId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.UserSessions.Add(session);
        await database.SaveChangesAsync();
        return session;
    }

    private static async Task SuspendTenantAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await database.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        tenant.Suspend();
        await database.SaveChangesAsync();
    }

    private static async Task SuspendMembershipAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await database.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        var membership = await database.TenantMemberships.SingleAsync(candidate => candidate.TenantId == tenantId);
        membership.Suspend(tenant);
        await database.SaveChangesAsync();
    }

    private static async Task ApplyFailureStateAsync(Guid userId, SessionFailure failure, ControlledTimeProvider clock)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await database.UserSessions.SingleAsync();
        switch (failure)
        {
            case SessionFailure.Revoked:
                session.Revoke(clock.GetUtcNow());
                break;
            case SessionFailure.IdleExpired:
                clock.Advance(TimeSpan.FromMinutes(31));
                break;
            case SessionFailure.AbsoluteExpired:
                clock.Advance(TimeSpan.FromHours(13));
                break;
            case SessionFailure.Unconfirmed:
                (await database.Users.SingleAsync(user => user.Id == userId)).EmailConfirmed = false;
                break;
            case SessionFailure.LockedOut:
                var user = await database.Users.SingleAsync(user => user.Id == userId);
                user.LockoutEnabled = true;
                user.LockoutEnd = clock.GetUtcNow().AddMinutes(15);
                break;
        }

        await database.SaveChangesAsync();
    }

    private static AuthenticationTicket UnprotectTicket(ProductionHarness harness, string cookie)
    {
        var options = harness.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        return options.TicketDataFormat.Unprotect(cookie.Split('=', 2)[1])!;
    }

    private static string ProtectTicket(ProductionHarness harness, Guid identityId, Guid sessionId)
    {
        var options = harness.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, identityId.ToString()),
            new Claim(ClaimTypes.Sid, sessionId.ToString())
        ], IdentityConstants.ApplicationScheme));
        return $"__Host-ia-auth={options.TicketDataFormat.Protect(new AuthenticationTicket(principal, new AuthenticationProperties(), IdentityConstants.ApplicationScheme))}";
    }

    private static HttpRequestMessage LoginRequest(string host, string email, string password, string antiforgery, string? forwardedFor)
    {
        var request = JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery);
        // TestServer reports no remote address; the trusted first hop of X-Forwarded-For simulates the client address.
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, int maxRetryAfterSeconds, params string[] secrets)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
        var retryAfter = response.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull("the 429 must carry a delta-seconds Retry-After header");
        retryAfter.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        retryAfter.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(maxRetryAfterSeconds));
        var text = await response.Content.ReadAsStringAsync();
        var payload = System.Text.Json.JsonDocument.Parse(text).RootElement;
        payload.GetProperty("code").GetString().ShouldBe("rate_limit_exceeded");
        payload.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        foreach (var property in new[] { "success", "data", "error", "errors" })
        {
            payload.TryGetProperty(property, out _).ShouldBeFalse($"{property} must not appear in a Problem Details body");
        }

        foreach (var secret in secrets)
        {
            text.ShouldNotContain(secret, Case.Insensitive);
        }
    }

    private static async Task<ApplicationUser> GetUserAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == identityId);
    }

    public enum NeutralOutcome { UnknownAccount, UnconfirmedAccount, LockedAccount, WrongPassword, ValidCredentials }
    public enum SessionFailure { Revoked, IdleExpired, AbsoluteExpired, Unconfirmed, LockedOut }
    public enum CookieReferenceFailure { DeletedSession, DeletedUser, MismatchedIdentityAndSession }
    public enum EmptyClaim { Sid, NameIdentifier, Both }
    private sealed record AntiforgeryPair(string RequestToken, string Cookie);

    private sealed class ControlledTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed class ProductionHarness(WebApiFactory factory, HttpClient client) : IDisposable
    {
        public WebApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
