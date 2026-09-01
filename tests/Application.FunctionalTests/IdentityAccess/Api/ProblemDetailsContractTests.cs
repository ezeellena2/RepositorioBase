using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class ProblemDetailsContractTests : TestBase
{
    [Test]
    public async Task Authorized_endpoints_return_semantic_200_201_location_and_204_shapes()
    {
        await TestApp.RunAsDefaultUserAsync();

        var get = await FunctionalTestSetup.HttpClient.GetAsync("/api/TodoLists");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var create = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoLists", new { title = "HTTP list" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        create.Headers.Location.ShouldNotBeNull();
        var id = await create.Content.ReadFromJsonAsync<int>();
        id.ShouldBeGreaterThan(0);

        var update = await FunctionalTestSetup.HttpClient.PutAsJsonAsync($"/api/TodoLists/{id}", new { id, title = "HTTP list updated" });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await update.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Validation_failure_is_an_external_400_with_field_errors_only()
    {
        await TestApp.RunAsDefaultUserAsync();

        var response = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoLists", new { });
        var payload = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed", hasErrors: true);

        payload.GetProperty("errors").GetProperty("Title").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Route_and_body_identifier_mismatch_is_an_external_400_problem_details_response()
    {
        await TestApp.RunAsDefaultUserAsync();

        var response = await FunctionalTestSetup.HttpClient.PutAsJsonAsync("/api/TodoLists/41", new { id = 42, title = "mismatch" });

        var payload = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "route_body_id_mismatch", hasErrors: true);
        payload.GetProperty("errors").TryGetProperty("id", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Authenticated_identity_without_endpoint_permission_gets_403_problem_details()
    {
        await TestApp.RunAsDefaultUserAsync();
        TestApp.SetHttpAuthorizationGranted(false);

        var response = await FunctionalTestSetup.HttpClient.GetAsync("/api/TodoLists");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "permission_denied");
    }

    [Test]
    public async Task Authentication_challenge_preserves_the_framework_authenticate_header()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/api/TodoLists");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        response.Headers.WwwAuthenticate.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Production_cookie_authentication_with_a_stale_application_identity_returns_a_safe_401_without_redirect()
    {
        using var factory = new WebApiFactory(
            FunctionalTestSetup.ConnectionString,
            Environments.Production,
            useTestAuthentication: false,
            useStaleApplicationUser: true);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var email = $"stale-{Guid.NewGuid():N}@example.test";
        const string password = "Testing1234!";

        var register = await client.PostAsJsonAsync("/api/Users/register", new { email, password });
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/Users/login?useCookies=true", new { email, password });
        login.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/TodoLists");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        response.Headers.Location.ShouldBeNull("a cookie challenge must not turn the API problem response into a login redirect");
        response.Headers.WwwAuthenticate.ShouldBeEmpty("the configured cookie scheme does not emit a bearer challenge header");
    }

    [TestCase("{")]
    [TestCase("{\"title\": 123")]
    public async Task Malformed_json_is_a_safe_external_400_problem(string malformedJson)
    {
        await TestApp.RunAsDefaultUserAsync();
        using var content = new StringContent(malformedJson, System.Text.Encoding.UTF8, "application/json");

        var response = await FunctionalTestSetup.HttpClient.PostAsync("/api/TodoLists", content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Test]
    public async Task Invalid_route_value_is_a_safe_external_400_problem()
    {
        await TestApp.RunAsDefaultUserAsync();
        using var content = JsonContent.Create(new { id = 1, title = "route value" });

        var response = await FunctionalTestSetup.HttpClient.PutAsync("/api/TodoLists/not-an-integer", content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [TestCase("{")]
    [TestCase("{\"title\": 123")]
    public async Task Production_malformed_json_is_a_safe_external_400_problem(string malformedJson)
    {
        await TestApp.RunAsDefaultUserAsync();
        using var factory = new WebApiFactory(FunctionalTestSetup.ConnectionString, Environments.Production);
        using var client = factory.CreateClient();
        using var content = new StringContent(malformedJson, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/TodoLists", content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Test]
    public async Task Production_invalid_route_value_is_a_safe_external_400_problem()
    {
        await TestApp.RunAsDefaultUserAsync();
        using var factory = new WebApiFactory(FunctionalTestSetup.ConnectionString, Environments.Production);
        using var client = factory.CreateClient();
        using var content = JsonContent.Create(new { id = 1, title = "route value" });

        var response = await client.PutAsync("/api/TodoLists/not-an-integer", content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Test]
    public async Task Missing_resource_stays_404_when_a_client_spoofs_a_tenant_header()
    {
        await TestApp.RunAsDefaultUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/TodoLists/999999")
        {
            Content = JsonContent.Create(new { id = 999999, title = "not present" })
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Test]
    public async Task Real_postgresql_xmin_conflict_is_an_external_409_without_internal_result_fields()
    {
        await TestApp.RunAsDefaultUserAsync();
        var list = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoLists", new { title = "concurrency HTTP list" });
        var listId = await list.Content.ReadFromJsonAsync<int>();
        var createItem = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoItems", new { listId, title = "concurrency HTTP item" });
        var itemId = await createItem.Content.ReadFromJsonAsync<int>();
        TestApp.ForceTodoItemConcurrencyConflict();

        var response = await FunctionalTestSetup.HttpClient.PatchAsJsonAsync($"/api/TodoItems/UpdateDetail/{itemId}", new { id = itemId, listId, priority = 1, note = "stale write" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "todo_item_concurrency_conflict");
    }

    [Test]
    public async Task Unsupported_todo_item_write_concurrency_is_redacted_as_a_500_not_a_todo_detail_conflict()
    {
        await TestApp.RunAsDefaultUserAsync();
        var list = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoLists", new { title = "unsupported concurrency list" });
        var listId = await list.Content.ReadFromJsonAsync<int>();
        var createItem = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoItems", new { listId, title = "unsupported concurrency item" });
        var itemId = await createItem.Content.ReadFromJsonAsync<int>();
        TestApp.ForceTodoItemConcurrencyConflict();

        var response = await FunctionalTestSetup.HttpClient.PutAsJsonAsync($"/api/TodoItems/{itemId}", new { id = itemId, listId, title = "stale update", done = false });

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal_server_error");
    }

    [Test]
    public async Task Unexpected_exception_is_a_sanitized_500_problem_details_response()
    {
        await TestApp.RunAsDefaultUserAsync();
        TestApp.ForceUnexpectedFailure();

        var response = await FunctionalTestSetup.HttpClient.PostAsJsonAsync("/api/TodoLists", new { title = "unexpected failure" });
        var payload = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal_server_error");
        var text = payload.GetRawText();

        text.ShouldNotContain("password");
        text.ShouldNotContain("provider");
        text.ShouldNotContain("stack");
        text.ShouldNotContain("exception");
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
