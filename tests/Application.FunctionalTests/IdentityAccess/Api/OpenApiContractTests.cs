using System.Text.Json;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class OpenApiContractTests : TestBase
{
    [Test]
    public async Task Registration_endpoints_declare_bodyless_success_and_problem_details_contracts()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        var antiforgery = paths.GetProperty("/api/identity/antiforgery").GetProperty("get").GetProperty("responses");
        antiforgery.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
        var register = paths.GetProperty("/api/identity/organizations/register").GetProperty("post").GetProperty("responses");
        register.TryGetProperty("202", out _).ShouldBeTrue();
        foreach (var status in new[] { "400", "401", "409", "500" }) register.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        AssertProblemCodes(register, "400", "antiforgery_validation_failed", "invalid_registration");
        AssertProblemCodes(register, "401", "invalid_session");
        AssertProblemCodes(register, "409", "registration_conflict");
        AssertProblemCodes(register, "500", "internal_server_error");
        var confirm = paths.GetProperty("/api/identity/confirm-email").GetProperty("post").GetProperty("responses");
        confirm.TryGetProperty("204", out _).ShouldBeTrue();
        confirm.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(confirm, "400", "antiforgery_validation_failed", "invalid_confirmation");
        AssertProblemCodes(confirm, "409", "registration_conflict");
        AssertProblemCodes(confirm, "500", "internal_server_error");
    }

    [Test]
    public async Task Identity_context_contracts_declare_exact_success_and_problem_code_arrays()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        var context = paths.GetProperty("/api/identity/context").GetProperty("get").GetProperty("responses");
        context.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
        AssertProblemCodes(context, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(context, "500", "internal_server_error");
        foreach (var status in new[] { "400", "403", "404", "409", "429" }) context.TryGetProperty(status, out _).ShouldBeFalse();

        var select = paths.GetProperty("/api/identity/context/tenant").GetProperty("put").GetProperty("responses");
        select.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
        AssertProblemCodes(select, "400", "antiforgery_validation_failed", "invalid_request");
        AssertProblemCodes(select, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(select, "403", "permission_denied");
        AssertProblemCodes(select, "500", "internal_server_error");
        foreach (var status in new[] { "404", "409", "429" }) select.TryGetProperty(status, out _).ShouldBeFalse();
    }

    [Test]
    public async Task Identity_session_contracts_declare_bodyless_success_and_exact_problem_codes()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        var create = paths.GetProperty("/api/identity/sessions").GetProperty("post").GetProperty("responses");
        create.TryGetProperty("204", out _).ShouldBeTrue();
        create.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(create, "400", "antiforgery_validation_failed", "invalid_request");
        AssertProblemCodes(create, "429", "rate_limit_exceeded");
        create.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _).ShouldBeTrue("the login 429 must advertise Retry-After");
        AssertProblemCodes(create, "500", "internal_server_error");
        foreach (var status in new[] { "401", "403", "404", "409" }) create.TryGetProperty(status, out _).ShouldBeFalse($"POST sessions must not advertise {status}.");

        var revoke = paths.GetProperty("/api/identity/sessions/current").GetProperty("delete").GetProperty("responses");
        revoke.TryGetProperty("204", out _).ShouldBeTrue();
        revoke.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(revoke, "400", "antiforgery_validation_failed");
        AssertProblemCodes(revoke, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(revoke, "500", "internal_server_error");
        foreach (var status in new[] { "403", "404", "409", "429" }) revoke.TryGetProperty(status, out _).ShouldBeFalse($"DELETE sessions/current must not advertise {status}.");
    }

    [Test]
    public async Task Todo_item_detail_update_declares_204_and_problem_details_contracts()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var responses = root.GetProperty("paths").GetProperty("/api/TodoItems/UpdateDetail/{id}").GetProperty("patch").GetProperty("responses");

        responses.TryGetProperty("204", out _).ShouldBeTrue();
        foreach (var status in new[] { "400", "401", "403", "404", "409", "500" })
        {
            var content = responses.GetProperty(status).GetProperty("content");
            content.TryGetProperty("application/problem+json", out _).ShouldBeTrue($"{status} must declare Problem Details.");
        }

        var problemSchema = root.GetProperty("components").GetProperty("schemas").GetProperty("ApiProblemDetails");
        problemSchema.GetProperty("properties").TryGetProperty("code", out _).ShouldBeTrue();
        problemSchema.GetProperty("properties").TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Todo_list_read_contract_declares_only_its_supported_problem_statuses_and_codes()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/TodoLists").GetProperty("get");
        var responses = operation.GetProperty("responses");

        responses.TryGetProperty("200", out _).ShouldBeTrue();
        foreach (var status in new[] { "401", "403", "500" })
        {
            var problem = responses.GetProperty(status);
            problem.GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
            problem.TryGetProperty("x-problem-codes", out var codes).ShouldBeTrue();
            codes.EnumerateArray().Any(code => !string.IsNullOrWhiteSpace(code.GetString())).ShouldBeTrue();
        }

        responses.GetProperty("401").TryGetProperty("headers", out var headers).ShouldBeFalse("the configured cookie scheme does not guarantee a WWW-Authenticate challenge header");

        foreach (var unsupportedStatus in new[] { "400", "404", "409", "429" })
        {
            responses.TryGetProperty(unsupportedStatus, out _).ShouldBeFalse($"GET TodoLists must not advertise unsupported {unsupportedStatus}.");
        }
    }

    [Test]
    public async Task Todo_list_create_contract_declares_201_location_and_only_its_supported_errors()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var responses = root.GetProperty("paths").GetProperty("/api/TodoLists").GetProperty("post").GetProperty("responses");

        responses.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _).ShouldBeTrue();
        foreach (var status in new[] { "400", "401", "403", "500" })
        {
            responses.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        }

        foreach (var unsupportedStatus in new[] { "404", "409", "429" })
        {
            responses.TryGetProperty(unsupportedStatus, out _).ShouldBeFalse($"POST TodoLists must not advertise unsupported {unsupportedStatus}.");
        }
    }

    [TestCase("/api/TodoItems/{id}", "put")]
    [TestCase("/api/TodoLists/{id}", "put")]
    public async Task Update_contracts_declare_bodyless_204_and_their_explicit_problem_codes(string path, string method)
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var responses = root.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        var noContent = responses.GetProperty("204");
        noContent.TryGetProperty("content", out _).ShouldBeFalse("204 update responses must not advertise a body.");

        AssertProblemCodes(responses, "400", "validation_failed", "invalid_request", "route_body_id_mismatch");
        AssertProblemCodes(responses, "401", "authentication_required");
        AssertProblemCodes(responses, "403", "permission_denied");
        AssertProblemCodes(responses, "404", "not_found");
        AssertProblemCodes(responses, "500", "internal_server_error");
        responses.TryGetProperty("409", out _).ShouldBeFalse("plain update endpoints do not expose a concurrency result.");
    }

    private static void AssertProblemCodes(JsonElement responses, string status, params string[] expectedCodes)
    {
        var response = responses.GetProperty(status);
        response.GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        var codes = response.GetProperty("x-problem-codes").EnumerateArray().Select(element => element.GetString()).ToArray();
        codes.ShouldBe(expectedCodes);
    }
}
