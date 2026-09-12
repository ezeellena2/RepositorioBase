using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class ProblemDetailsContractTests : TestBase
{
    private const string SpaIndexSentinel = "functional-spa-index";

    [TestCase("status")]
    [TestCase("type")]
    [TestCase("title")]
    [TestCase("instance")]
    [TestCase("code")]
    [TestCase("traceId")]
    public void Shared_problem_reader_rejects_an_invalid_required_member(string member)
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = StatusCodes.Status400BadRequest,
            ["type"] = "about:blank",
            ["title"] = "Bad Request",
            ["instance"] = "/api/test",
            ["code"] = "invalid_request",
            ["traceId"] = "trace-test"
        };
        body[member] = member == "status" ? StatusCodes.Status409Conflict : string.Empty;
        using var response = ProblemResponse(body, HttpStatusCode.BadRequest);

        Assert.ThrowsAsync<Shouldly.ShouldAssertException>(
            async () => await IdentityHttpHarness.ReadProblemAsync(response));
    }

    [Test]
    public async Task Expected_problem_accepts_any_nonempty_title_instead_of_the_transport_reason_phrase()
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = StatusCodes.Status400BadRequest,
            ["type"] = "about:blank",
            ["title"] = "Localized request refusal",
            ["instance"] = "/api/test",
            ["code"] = "invalid_request",
            ["traceId"] = "trace-test"
        };
        using var response = ProblemResponse(body, HttpStatusCode.BadRequest);
        response.ReasonPhrase = "Transport reason phrase";

        await IdentityHttpHarness.AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Test]
    public void Dead_route_body_mismatch_contract_is_absent() =>
        typeof(ApiProblemMetadata).GetField("RouteBodyIdMismatch").ShouldBeNull();

    [Test]
    public void Validation_problem_normalizes_member_paths_to_wire_names_without_losing_message_order()
    {
        var errors = new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal)
        {
            ["Token"] = [
                new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>()),
                new ValidationErrorDetail(ValidationErrorCodes.TooLong, new Dictionary<string, int> { ["max"] = 12 })],
            ["token"] = [
                new ValidationErrorDetail(ValidationErrorCodes.TooLong, new Dictionary<string, int> { ["max"] = 12 }),
                new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>())],
            ["Items[0].Code"] = [
                new ValidationErrorDetail(ValidationErrorCodes.UnsupportedValue, new Dictionary<string, int>())]
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/platform/mfa/verify";

        var problem = new ApiProblemDetailsMapper().Create(
            context,
            new ApplicationError("validation_failed", ApplicationErrorCategory.Validation, validationErrors: errors));

        problem.Errors.ShouldNotBeNull();
        problem.Errors.Keys.ToArray().ShouldBe(["items[0].code", "token"]);
        var tokenErrors = problem.Errors["token"];
        tokenErrors.Select(detail => detail.Code).ShouldBe([
            ValidationErrorCodes.Invalid,
            ValidationErrorCodes.Required,
            ValidationErrorCodes.TooLong]);
        tokenErrors.Single(detail => detail.Code == ValidationErrorCodes.Invalid).Params.ShouldBeEmpty();
        tokenErrors.Single(detail => detail.Code == ValidationErrorCodes.Required).Params.ShouldBeEmpty();
        var tooLong = tokenErrors.Single(detail => detail.Code == ValidationErrorCodes.TooLong);
        tooLong.Params.Count.ShouldBe(1);
        tooLong.Params["max"].ShouldBe(12);
        var itemCode = problem.Errors["items[0].code"].ShouldHaveSingleItem();
        itemCode.Code.ShouldBe(ValidationErrorCodes.UnsupportedValue);
        itemCode.Params.ShouldBeEmpty();
    }

    [TestCase("GET", "/api/route-that-does-not-exist")]
    [TestCase("POST", "/api/route-that-does-not-exist")]
    [TestCase("OPTIONS", "/api/route-that-does-not-exist")]
    [TestCase("GET", "/api/tenants/not-a-guid/roles")]
    public async Task Unmatched_api_routes_return_the_shared_not_found_problem(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), $"https://api-routing.localhost{path}");
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var payload = await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");

        payload.GetProperty("instance").GetString().ShouldBe(path);
        payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["code", "instance", "status", "title", "traceId", "type"]);
        payload.GetRawText().ShouldNotContain("<html", Case.Insensitive);
    }

    [TestCase("PATCH", "/api/identity/antiforgery", "GET")]
    [TestCase("GET", "/api/identity/confirm-email", "POST")]
    public async Task A_known_api_path_with_the_wrong_method_keeps_the_framework_405(
        string method,
        string path,
        string allowedMethod)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), $"https://api-routing.localhost{path}");
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain(allowedMethod);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe(ApiProblemMetadata.ProblemContentType);
    }

    [Test]
    public async Task Head_for_an_unknown_api_route_keeps_the_404_headers_and_has_no_entity_body()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "https://api-routing.localhost/api/route-that-does-not-exist");
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ApiProblemMetadata.ProblemContentType);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [TestCase("/a-client-route-that-does-not-exist")]
    [TestCase("/apiary")]
    public async Task Unknown_non_api_client_routes_are_served_by_the_spa_fallback(string path)
    {
        using var response = await FunctionalTestSetup.HttpClient.GetAsync(
            $"https://api-routing.localhost{path}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html", path);
        (await response.Content.ReadAsStringAsync()).ShouldContain(SpaIndexSentinel);
    }

    [Test]
    public async Task Head_for_a_non_api_client_route_has_the_spa_success_headers_and_no_entity_body()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            "https://api-routing.localhost/a-client-route-that-does-not-exist");
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Content.Headers.ContentLength.ShouldNotBeNull();
        response.Content.Headers.ContentLength!.Value.ShouldBeGreaterThan(0);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task A_file_like_unknown_non_api_path_is_not_rewritten_to_the_spa_index()
    {
        using var response = await FunctionalTestSetup.HttpClient.GetAsync(
            "https://api-routing.localhost/missing.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(SpaIndexSentinel);
    }

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
            Content = JsonContent.Create(new { email = $"http-{suffix}@example.test", password = "Testing1234!", legalName = "HTTP Confirmation", cuit = "30-12345678-1" })
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

    [Test]
    public void Validation_problem_uses_camel_case_fields_and_safe_structured_details()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ApiProblemDetailsMapper>();
        var context = new DefaultHttpContext();
        var error = new ApplicationError(
            "validation_failed",
            ApplicationErrorCategory.Validation,
            validationErrors: new Dictionary<string, ValidationErrorDetail[]>
            {
                ["NewPassword"] = [new ValidationErrorDetail(
                    ValidationErrorCodes.TooLong,
                    new Dictionary<string, int> { ["max"] = 256 })]
            });

        var problem = mapper.Create(context, error);

        problem.Errors!.Keys.ShouldBe(new[] { "newPassword" });
        var detail = problem.Errors["newPassword"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.TooLong);
        detail.Params.Count.ShouldBe(1);
        detail.Params["max"].ShouldBe(256);
    }

    [Test]
    public void Validation_problem_merges_colliding_camel_case_fields_and_removes_duplicate_details()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ApiProblemDetailsMapper>();
        var error = new ApplicationError(
            "validation_failed",
            ApplicationErrorCategory.Validation,
            validationErrors: new Dictionary<string, ValidationErrorDetail[]>
            {
                ["NewPassword"] = [
                    new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>()),
                    new ValidationErrorDetail(ValidationErrorCodes.TooLong, new Dictionary<string, int> { ["max"] = 256 })],
                ["newPassword"] = [new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>())]
            });

        var errors = mapper.Create(new DefaultHttpContext(), error).Errors!;

        errors.Keys.ShouldBe(["newPassword"]);
        errors["newPassword"].Select(detail => detail.Code).ShouldBe([ValidationErrorCodes.Required, ValidationErrorCodes.TooLong]);
    }

    [Test]
    public void Validation_problem_collision_merge_deduplication_and_order_are_independent_of_producer_order()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ApiProblemDetailsMapper>();

        static ApplicationError Error(IEnumerable<KeyValuePair<string, ValidationErrorDetail[]>> entries) => new(
            "validation_failed",
            ApplicationErrorCategory.Validation,
            validationErrors: entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        var required = new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>());
        var shortLimit = new ValidationErrorDetail(ValidationErrorCodes.TooLong, new Dictionary<string, int> { ["max"] = 16 });
        var longLimit = new ValidationErrorDetail(ValidationErrorCodes.TooLong, new Dictionary<string, int> { ["max"] = 256 });
        var forward = Error([
            new("NewPassword", [longLimit, required]),
            new("newPassword", [shortLimit, required]),
        ]);
        var reverse = Error([
            new("newPassword", [required, shortLimit]),
            new("NewPassword", [required, longLimit]),
        ]);

        var first = mapper.Create(new DefaultHttpContext(), forward).Errors!;
        var second = mapper.Create(new DefaultHttpContext(), reverse).Errors!;

        JsonSerializer.Serialize(first).ShouldBe(JsonSerializer.Serialize(second));
        first.Keys.ShouldBe(["newPassword"]);
        first["newPassword"].Select(detail => (detail.Code, detail.Params.GetValueOrDefault("max")))
            .ShouldBe([
                (ValidationErrorCodes.Required, 0),
                (ValidationErrorCodes.TooLong, 16),
                (ValidationErrorCodes.TooLong, 256),
            ]);
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

    [Test]
    public async Task Expected_validation_authentication_and_authorization_refusals_write_no_error_record()
    {
        const string host = "https://expected-refusal-logs.localhost";
        var antiforgery = (await GetAntiforgeryAsync(host)).RequestToken;
        TestApp.ResetCapturedLogs();

        using var malformedRegistration = new HttpRequestMessage(
            HttpMethod.Post,
            $"{host}/api/identity/organizations/register")
        {
            Content = JsonContent.Create(new
            {
                email = "not-an-email",
                password = "short",
                legalName = "",
                cuit = "30-12345678-9"
            })
        };
        malformedRegistration.Headers.Add("Origin", host);
        malformedRegistration.Headers.Add("X-CSRF-TOKEN", antiforgery);
        await AssertProblemAsync(
            await FunctionalTestSetup.HttpClient.SendAsync(malformedRegistration),
            HttpStatusCode.BadRequest,
            "validation_failed",
            hasErrors: true);
        ErrorRecords().ShouldBeEmpty("an expected validation refusal is not an operational fault");

        TestApp.ResetCapturedLogs();
        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token = TestApp.RawTokenAt(2) }, antiforgery),
            HttpStatusCode.Unauthorized,
            "authentication_required");
        ErrorRecords().ShouldBeEmpty("an authentication challenge is not an operational fault");

        await TestApp.RunAsDefaultUserAsync();
        TestApp.SetHttpAuthorizationGranted(false);
        antiforgery = (await GetAntiforgeryAsync(host)).RequestToken;
        TestApp.ResetCapturedLogs();
        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token = TestApp.RawTokenAt(2) }, antiforgery),
            HttpStatusCode.Forbidden,
            "permission_denied");
        ErrorRecords().ShouldBeEmpty("an authorization refusal is not an operational fault");
    }

    [TestCase(true, "System.Text.Json.JsonException", "downstream-json-secret")]
    [TestCase(false, "Microsoft.AspNetCore.Http.BadHttpRequestException", "downstream-bad-request-secret")]
    public async Task Binding_shaped_exception_thrown_after_neutral_body_binding_is_a_safe_500(
        bool jsonFailure,
        string exceptionType,
        string secret)
    {
        var host = $"https://neutral-downstream-{jsonFailure.ToString().ToLowerInvariant()}.localhost";
        var antiforgery = (await GetAntiforgeryAsync(host)).RequestToken;
        TestApp.ResetCapturedLogs();
        if (jsonFailure)
        {
            TestApp.ForceDownstreamJsonFailure();
        }
        else
        {
            TestApp.ForceDownstreamBadRequestFailure();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/sessions")
        {
            Content = JsonContent.Create(new { email = "unknown@example.test", password = "not-a-secret" })
        };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var problem = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal_server_error");
        var record = ErrorRecords().ShouldHaveSingleItem(
            "a downstream binding-shaped failure belongs only to the terminal boundary");
        record.ShouldStartWith("[Error] CleanArchitecture.Web.Infrastructure.ProblemDetailsExceptionHandler:");
        record.ShouldContain("unexpected_failure");
        record.ShouldContain(problem.GetProperty("traceId").GetString()!);
        record.ShouldContain(exceptionType);
        record.ShouldNotContain(secret);
        TestApp.CapturedLogs.ShouldAllBe(entry => !entry.Contains(secret, StringComparison.Ordinal));
    }

    [Test]
    public async Task Terminal_exception_boundary_covers_recovery_admission_before_files_and_endpoints()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness(
            configureTestServices: services =>
            {
                services.RemoveAll<IRecoveryAdmission>();
                services.AddSingleton<IRecoveryAdmission, ThrowingRecoveryAdmission>();
            });
        TestApp.ResetCapturedLogs();

        using var response = await harness.Client.GetAsync(
            "https://terminal-boundary.localhost/api/identity/antiforgery");

        var problem = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal_server_error");
        var record = ErrorRecords().ShouldHaveSingleItem(
            "the terminal exception boundary must also own failures from recovery admission");
        record.ShouldStartWith("[Error] CleanArchitecture.Web.Infrastructure.ProblemDetailsExceptionHandler:");
        record.ShouldContain("unexpected_failure");
        record.ShouldContain(problem.GetProperty("traceId").GetString()!);
        record.ShouldContain("System.InvalidOperationException");
        record.ShouldNotContain(ThrowingRecoveryAdmission.Secret);
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(ThrowingRecoveryAdmission.Secret, StringComparison.Ordinal));
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
        var unusableToken = TestApp.RawTokenAt(2);

        await AssertProblemAsync(
            await SendInvitationAsync(host, "/api/invitations/accept", new { token = unusableToken }, antiforgery),
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
        var submittedAddress = $"pii-sentinel-{Guid.NewGuid():N}@example.test";
        var faultedRequestToken = $"token-sentinel-{Guid.NewGuid():N}";
        // InviteMemberRequest deliberately has no token member. This extra JSON property exercises raw-body
        // non-logging; the PostgresException/42P01 assertions below prove binding still reached provider failure.
        var faultedRequest = new
        {
            email = submittedAddress,
            roleIds = new[] { organization.RoleId },
            token = faultedRequestToken
        };
        JsonSerializer.Serialize(faultedRequest).ShouldContain($"\"token\":\"{faultedRequestToken}\"");
        TestApp.ResetCapturedLogs();
        TestApp.ForceUnexpectedFailure();
        var faulted = await SendInvitationAsync(
            host,
            $"/api/tenants/{organization.TenantId.Value}/invitations",
            faultedRequest,
            issuerAntiforgery);

        var payload = await AssertProblemAsync(faulted, HttpStatusCode.InternalServerError, "internal_server_error");
        var traceId = payload.GetProperty("traceId").GetString()!;
        var body = payload.GetRawText();
        const string efFailureMessage = "An error occurred while saving the entity changes";
        body.ShouldNotContain(faultedRequestToken, Case.Insensitive);
        body.ShouldNotContain("password", Case.Insensitive);
        // A safe 500 may carry a detail or none at all; what it may never carry is anything about the fault.
        if (payload.TryGetProperty("detail", out var detail))
        {
            var text = detail.GetString() ?? string.Empty;
            text.ShouldNotContain("Exception", Case.Insensitive);
            text.ShouldNotContain("password", Case.Insensitive);
        }

        var record = ErrorRecords().ShouldHaveSingleItem(
            "the terminal boundary must own the only Error record, including framework and provider logs");
        record.ShouldStartWith("[Error] CleanArchitecture.Web.Infrastructure.ProblemDetailsExceptionHandler:");
        record.ShouldContain("unexpected_failure");
        record.ShouldContain(traceId);
        record.ShouldContain("invitations");
        record.ShouldContain("Npgsql.PostgresException");
        record.ShouldContain("42P01");
        record.ShouldContain("Npgsql");
        record.ShouldContain("ExceptionTypes");
        record.ShouldContain("StackFrames");
        record.ShouldNotContain(TestProviderFailureInterceptor.SecretSentinel);
        record.ShouldNotContain(TestProviderFailureInterceptor.ParameterName);
        record.ShouldNotContain(TestProviderFailureInterceptor.ParameterSentinel);
        record.ShouldNotContain("does not exist", Case.Insensitive);
        record.ShouldNotContain(efFailureMessage, Case.Insensitive);
        record.ShouldNotContain("SELECT * FROM", Case.Insensitive);
        record.ShouldNotContain(submittedAddress);
        record.ShouldNotContain(faultedRequestToken);
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(TestProviderFailureInterceptor.SecretSentinel, StringComparison.Ordinal),
            "neither the provider message nor its failed SQL may escape through any logger");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(TestProviderFailureInterceptor.ParameterName, StringComparison.Ordinal) &&
                     !entry.Contains(TestProviderFailureInterceptor.ParameterSentinel, StringComparison.Ordinal),
            "neither the failed command's parameter name nor value may escape through any logger");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains("does not exist", StringComparison.OrdinalIgnoreCase),
            "the raw PostgreSQL message must not escape through any logger");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(efFailureMessage, StringComparison.OrdinalIgnoreCase) &&
                     !entry.Contains("Exception data:", StringComparison.OrdinalIgnoreCase) &&
                     !entry.Contains("MessageText:", StringComparison.OrdinalIgnoreCase) &&
                     !entry.Contains("Boolean async", StringComparison.Ordinal),
            "no exception message, Data values or raw stack arguments may escape through any logger");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains($"SELECT * FROM \"{TestProviderFailureInterceptor.SecretSentinel}\" WHERE", StringComparison.Ordinal),
            "the failed SQL text must not escape through any logger");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(submittedAddress, StringComparison.OrdinalIgnoreCase),
            "the submitted address must not be recorded by any provider");
        TestApp.CapturedLogs.ShouldAllBe(
            entry => !entry.Contains(faultedRequestToken, StringComparison.Ordinal),
            "the submitted token must not be recorded by any provider");
    }

    private static string[] ErrorRecords() =>
        TestApp.CapturedLogs
            .Where(entry => entry.StartsWith("[Error] ", StringComparison.Ordinal))
            .ToArray();

    private sealed class ThrowingRecoveryAdmission : IRecoveryAdmission
    {
        internal const string Secret = "recovery-admission-secret";

        public RecoveryAdmission Current => throw new InvalidOperationException(Secret);
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

    private static HttpResponseMessage ProblemResponse(
        IReadOnlyDictionary<string, object?> body,
        HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body))
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
        return response;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string code, bool hasErrors = false)
    {
        var payload = await IdentityHttpHarness.AssertProblemAsync(response, statusCode, code, hasErrors);
        payload.TryGetProperty("success", out _).ShouldBeFalse();
        payload.TryGetProperty("data", out _).ShouldBeFalse();
        payload.TryGetProperty("error", out _).ShouldBeFalse();
        return payload;
    }
}
