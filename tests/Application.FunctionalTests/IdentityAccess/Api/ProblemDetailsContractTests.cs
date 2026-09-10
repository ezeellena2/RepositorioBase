using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class ProblemDetailsContractTests : TestBase
{
    [Test]
    public async Task Registration_antiforgery_endpoint_returns_no_store_and_the_host_cookie_contract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/identity/antiforgery");
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.TryGetValues("Set-Cookie", out var values).ShouldBeTrue();
        var cookie = values!.Single();
        cookie.ShouldContain("__Host-XSRF-TOKEN=");
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);
        var payload = await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>();
        payload!.RequestToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Confirmation_with_a_real_antiforgery_request_and_an_unknown_token_returns_invalid_confirmation_problem_details()
    {
        using var antiforgeryRequest = new HttpRequestMessage(HttpMethod.Get, "https://confirmation.localhost/api/identity/antiforgery");
        var antiforgeryResponse = await FunctionalTestSetup.HttpClient.SendAsync(antiforgeryRequest);
        antiforgeryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var antiforgery = await antiforgeryResponse.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>();
        antiforgery!.RequestToken.ShouldNotBeNullOrWhiteSpace();

        using var confirmation = new HttpRequestMessage(HttpMethod.Post, "https://confirmation.localhost/api/identity/confirm-email")
        {
            Content = JsonContent.Create(new { token = "unknown-confirmation-token" })
        };
        confirmation.Headers.Add("Origin", "https://confirmation.localhost");
        confirmation.Headers.Add("X-CSRF-TOKEN", antiforgery.RequestToken);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(confirmation);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_confirmation");
    }

    [Test]
    public async Task Identity_mutations_reject_every_invalid_origin_or_antiforgery_pair_before_business_effects()
    {
        var host = $"https://antiforgery-{Guid.NewGuid():N}.localhost";
        var missingCookieHost = $"https://no-cookie-{Guid.NewGuid():N}.localhost";
        var antiforgery = await GetAntiforgeryAsync(host);
        var initialOutboxCount = await TestApp.CountAsync<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage>();

        var rejectedRequests = new[]
        {
            CreateConfirmationRequest(host, null, antiforgery.RequestToken),
            CreateConfirmationRequest(host, "https://cross-origin.localhost", antiforgery.RequestToken),
            CreateConfirmationRequest(host, host.Replace("https://", "http://", StringComparison.Ordinal), antiforgery.RequestToken),
            CreateConfirmationRequest(host, $"{host}:444", antiforgery.RequestToken),
            CreateConfirmationRequest(missingCookieHost, missingCookieHost, antiforgery.RequestToken),
            CreateConfirmationRequest(host, host, null),
            CreateConfirmationRequest(host, host, "wrong-request-token")
        };

        foreach (var request in rejectedRequests)
        {
            using (request)
            {
                var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
                await AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery_validation_failed");
            }
        }

        (await TestApp.CountAsync<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage>()).ShouldBe(initialOutboxCount);
    }

    [Test]
    public async Task Antiforgery_cookie_containers_remain_host_isolated_and_rebootstrap_does_not_break_the_original_pair()
    {
        var firstHost = $"https://first-antiforgery-{Guid.NewGuid():N}.localhost";
        var secondHost = $"https://second-antiforgery-{Guid.NewGuid():N}.localhost";
        var first = await GetAntiforgeryAsync(firstHost);
        var second = await GetAntiforgeryAsync(secondHost);

        using var crossHostRequest = CreateConfirmationRequest(firstHost, firstHost, second.RequestToken);
        var crossHostResponse = await FunctionalTestSetup.HttpClient.SendAsync(crossHostRequest);
        await AssertProblemAsync(crossHostResponse, HttpStatusCode.BadRequest, "antiforgery_validation_failed");

        using var rebootstrap = new HttpRequestMessage(HttpMethod.Get, $"{firstHost}/api/identity/antiforgery");
        var rebootstrapResponse = await FunctionalTestSetup.HttpClient.SendAsync(rebootstrap);
        rebootstrapResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await rebootstrapResponse.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>();
        refreshed!.RequestToken.ShouldNotBeNullOrWhiteSpace();

        using var validRequest = CreateConfirmationRequest(firstHost, firstHost, refreshed.RequestToken);
        var validResponse = await FunctionalTestSetup.HttpClient.SendAsync(validRequest);
        await AssertProblemAsync(validResponse, HttpStatusCode.BadRequest, "invalid_confirmation");
    }

    [Test]
    public async Task Registration_and_confirmation_over_http_activate_once_and_replay_bodyless_success()
    {
        const string host = "https://confirmation-success.localhost";
        var suffix = Guid.NewGuid().ToString("N");
        var antiforgery = await GetAntiforgeryAsync(host);
        using var registration = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/organizations/register")
        {
            Content = JsonContent.Create(new { email = $"http-{suffix}@example.test", password = "Testing1234!", legalName = "HTTP Confirmation", cuit = "30-12345678-9" })
        };
        registration.Headers.Add("Origin", host);
        registration.Headers.Add("X-CSRF-TOKEN", antiforgery.RequestToken);

        var registrationResponse = await FunctionalTestSetup.HttpClient.SendAsync(registration);
        registrationResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var confirmation = await SendConfirmationAsync(host, antiforgery.RequestToken);
        confirmation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await confirmation.Content.ReadAsStringAsync()).ShouldBeEmpty();
        var replay = await SendConfirmationAsync(host, antiforgery.RequestToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await replay.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeTrue();
        (await TestApp.ListAsync<Tenant>()).Single().Status.ShouldBe(TenantStatus.Active);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.Active);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
    }

    [Test]
    public async Task Rate_limit_problem_writes_retry_after_without_a_universal_envelope()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Web.Infrastructure.ApiProblemDetailsMapper>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var result = Result.Failure(new ApplicationError("rate_limit_exceeded", ApplicationErrorCategory.RateLimited, retryAfterSeconds: 30));
        await result.ToHttpResult(context, mapper).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var payload = JsonDocument.Parse(context.Response.Body).RootElement;

        context.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        context.Response.ContentType.ShouldBe("application/problem+json");
        context.Response.Headers.RetryAfter.ToString().ShouldBe("30");
        payload.GetProperty("code").GetString().ShouldBe("rate_limit_exceeded");
        payload.TryGetProperty("success", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Every failure the three invitation routes can produce, at the boundary, as RFC 9457 with the code each
    /// route declares. What this adds over the OpenAPI test is that the runtime agrees with the document: a
    /// declared code nothing emits, or an emitted code nothing declares, is drift either way (IA-REQ-038).
    /// </summary>
    [Test]
    public async Task Invitation_routes_answer_their_declared_problem_codes_at_runtime()
    {
        const string host = "https://invitation-problems.localhost";
        var antiforgery = (await GetAntiforgeryAsync(host)).RequestToken;

        // Anonymous, on the two authorized routes.
        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token = TestApp.RawTokenAt(2) }, antiforgery),
            HttpStatusCode.Unauthorized,
            "authentication_required");
        await AssertProblemAsync(
            await SendInvitationAsync(host, $"/api/tenants/{Guid.NewGuid()}/invitations", new { email = "a@example.test", roleIds = new[] { Guid.NewGuid() } }, antiforgery),
            HttpStatusCode.Unauthorized,
            "authentication_required");

        // Authenticated but refused at the endpoint.
        await TestApp.RunAsDefaultUserAsync();
        TestApp.SetHttpAuthorizationGranted(false);
        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token = TestApp.RawTokenAt(2) }, antiforgery),
            HttpStatusCode.Forbidden,
            "permission_denied");

        // Missing or mismatched antiforgery, on the public route, before any business decision.
        TestApp.SetHttpAuthorizationGranted(true);
        using var withoutAntiforgery = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/invitations/register")
        {
            Content = JsonContent.Create(new { token = TestApp.RawTokenAt(2), password = "Testing1234!" })
        };
        withoutAntiforgery.Headers.Add("Origin", host);
        await AssertProblemAsync(
            await FunctionalTestSetup.HttpClient.SendAsync(withoutAntiforgery),
            HttpStatusCode.BadRequest,
            "antiforgery_validation_failed");
    }

    /// <summary>
    /// A token that resolves to nothing is refused as <c>invalid_invitation</c>, and an unexpected fault on the
    /// same route is a sanitized 500 that discloses nothing — including the token the caller submitted.
    /// </summary>
    [Test]
    public async Task An_unusable_invitation_token_is_a_400_and_an_unexpected_fault_is_a_safe_500()
    {
        const string host = "https://invitation-faults.localhost";
        var identityId = await TestApp.RunAsDefaultUserAsync();
        TestApp.SetUserId(identityId);
        TestApp.SetHttpAuthorizationGranted(true);
        TestApp.SetApplicationPermissionGranted(true);

        // The pair is bootstrapped after the authentication state settles: the server rotates it whenever that
        // state changes, so one fetched earlier would be rejected before the business decision under test.
        var antiforgery = (await GetAntiforgeryAsync(host)).RequestToken;
        var token = TestApp.RawTokenAt(2);

        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token }, antiforgery),
            HttpStatusCode.BadRequest,
            "invalid_invitation");

        // The fault is armed on a route that actually persists. Acceptance of an unknown token decides and
        // returns before any save, so arming it there would have proved nothing and read as a passing 400.
        var organization = await Invitations.InvitationScenario.SeedOrganizationAsync(
            Application.IdentityAccess.Authorization.Permissions.MembersInvite);
        TestApp.SetUserId(organization.InviterIdentityId);
        TestApp.SetCurrentTenant(organization.TenantId);
        // Switching the acting identity rotates the antiforgery pair, so the second request bootstraps its own.
        var issuerAntiforgery = (await GetAntiforgeryAsync(host)).RequestToken;
        TestApp.ForceUnexpectedFailure();
        var faulted = await SendInvitationAsync(
            host,
            $"/api/tenants/{organization.TenantId.Value}/invitations",
            new { email = $"invitee-{Guid.NewGuid():N}@example.test", roleIds = new[] { organization.RoleId } },
            issuerAntiforgery);

        var payload = await AssertProblemAsync(faulted, HttpStatusCode.InternalServerError, "internal_server_error");
        var body = payload.GetRawText();
        body.ShouldNotContain(token, Case.Insensitive);
        body.ShouldNotContain("password", Case.Insensitive);
        // A safe 500 may carry a detail or none at all; what it may never carry is anything about the fault.
        if (payload.TryGetProperty("detail", out var detail))
        {
            var text = detail.GetString() ?? string.Empty;
            text.ShouldNotContain("Exception", Case.Insensitive);
            text.ShouldNotContain("password", Case.Insensitive);
        }
    }

    private static Task<HttpResponseMessage> SendInvitationAsync(string host, string path, object body, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{host}{path}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);
        return FunctionalTestSetup.HttpClient.SendAsync(request);
    }

    private static async Task<CleanArchitecture.Web.Endpoints.AntiforgeryResponse> GetAntiforgeryAsync(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var antiforgery = await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>();
        antiforgery!.RequestToken.ShouldNotBeNullOrWhiteSpace();
        return antiforgery;
    }

    private static Task<HttpResponseMessage> SendConfirmationAsync(string host, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/confirm-email")
        {
            Content = JsonContent.Create(new { token = TestApp.GetRegistrationRawToken() })
        };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);
        return FunctionalTestSetup.HttpClient.SendAsync(request);
    }

    private static HttpRequestMessage CreateConfirmationRequest(string requestHost, string? origin, string? antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{requestHost}/api/identity/confirm-email")
        {
            Content = JsonContent.Create(new { token = "unknown-confirmation-token" })
        };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (antiforgeryToken is not null) request.Headers.Add("X-CSRF-TOKEN", antiforgeryToken);
        return request;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string code, bool hasErrors = false)
    {
        response.StatusCode.ShouldBe(statusCode);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement.Clone();
        payload.GetProperty("code").GetString().ShouldBe(code);
        string.IsNullOrWhiteSpace(payload.GetProperty("traceId").GetString()).ShouldBeFalse();
        payload.TryGetProperty("success", out _).ShouldBeFalse();
        payload.TryGetProperty("data", out _).ShouldBeFalse();
        payload.TryGetProperty("error", out _).ShouldBeFalse();
        payload.TryGetProperty("errors", out _).ShouldBe(hasErrors);
        return payload;
    }
}
