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
        // Still declared, and still produced — but now only for an authenticated caller whose own CUIT is already
        // registered. An anonymous caller is answered neutrally in that case, so that a taken address and an
        // untaken one cannot be told apart (IA-REQ-003). Do not delete this as a dead declaration.
        AssertProblemCodes(register, "409", "registration_conflict");
        AssertProblemCodes(register, "500", "internal_server_error");
        var confirm = paths.GetProperty("/api/identity/confirm-email").GetProperty("post").GetProperty("responses");
        confirm.TryGetProperty("204", out _).ShouldBeTrue();
        confirm.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(confirm, "400", "antiforgery_validation_failed", "invalid_confirmation");
        AssertProblemCodes(confirm, "409", "registration_conflict");
        AssertProblemCodes(confirm, "500", "internal_server_error");
    }

    /// <summary>
    /// The Platform gates and directories (IA-REQ-041/045).
    /// <para>
    /// Two additions carry a decision each. The routes that accept an authenticator code declare a bounded-attempt
    /// refusal, because a client that cannot tell "wrong code" from "stop asking" will keep asking; the ones that
    /// accept no code do not, so the declaration says which routes are bounded. And every directory declares the
    /// second-factor refusal, because holding the permission is no longer enough to read one.
    /// </para>
    /// </summary>
    [Test]
    public async Task Platform_contracts_declare_the_bounded_code_gates_and_the_second_factor_directories()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        foreach (var route in new[] { "/api/platform/mfa/verify", "/api/platform/mfa/step-up" })
        {
            var gate = paths.GetProperty(route).GetProperty("post").GetProperty("responses");
            AssertProblemCodes(gate, "429", "rate_limit_exceeded");
            gate.GetProperty("429").GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        }

        foreach (var route in new[] { "/api/platform/mfa/enroll", "/api/platform/mfa/recovery-acknowledge" })
        {
            paths.GetProperty(route).GetProperty("post").GetProperty("responses")
                .TryGetProperty("429", out _).ShouldBeFalse($"{route} accepts no code, so it is not the bounded gate.");
        }

        foreach (var route in new[]
                 {
                     "/api/platform/organizations",
                     "/api/platform/identities",
                     "/api/platform/admins",
                     "/api/platform/audit"
                 })
        {
            var directory = paths.GetProperty(route).GetProperty("get").GetProperty("responses");
            AssertProblemCodes(directory, "401", "authentication_required", "invalid_session", "recent_mfa_required");
        }
    }

    /// <summary>
    /// The three invitation routes of SPEC section 6. What is asserted is the whole declaration, including the
    /// statuses each route must NOT advertise: an over-declared contract tells a client to handle an outcome the
    /// runtime never produces, which is drift in the direction the tests are least likely to notice.
    /// </summary>
    [Test]
    public async Task Invitation_contracts_declare_exact_success_shapes_headers_and_problem_codes()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        var issue = paths.GetProperty("/api/tenants/{tenantId}/invitations").GetProperty("post").GetProperty("responses");
        issue.GetProperty("201").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
        issue.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _).ShouldBeTrue("a created invitation must advertise where it lives");
        AssertProblemCodes(issue, "400", "antiforgery_validation_failed", "invalid_invitation");
        AssertProblemCodes(issue, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(issue, "403", "permission_denied");
        AssertProblemCodes(issue, "404", "not_found");
        AssertProblemCodes(issue, "409", "invitation_conflict");
        AssertProblemCodes(issue, "500", "internal_server_error");
        issue.TryGetProperty("429", out _).ShouldBeFalse("issuing is not rate limited in this increment");
        issue.TryGetProperty("202", out _).ShouldBeFalse();

        // Registration is the neutral half: bodyless, and it advertises no status that would let a caller tell a
        // live token from a dead one (IA-REQ-016, SPEC section 6).
        var register = paths.GetProperty("/api/invitations/register").GetProperty("post").GetProperty("responses");
        register.TryGetProperty("202", out _).ShouldBeTrue();
        register.GetProperty("202").TryGetProperty("content", out _).ShouldBeFalse("the neutral 202 carries no body");
        AssertProblemCodes(register, "400", "antiforgery_validation_failed", "invalid_invitation");
        AssertProblemCodes(register, "500", "internal_server_error");
        foreach (var status in new[] { "401", "403", "404", "409", "429" })
        {
            register.TryGetProperty(status, out _).ShouldBeFalse($"the public registration route must not advertise {status}.");
        }

        var accept = paths.GetProperty("/api/invitations/accept").GetProperty("post").GetProperty("responses");
        accept.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
        AssertProblemCodes(accept, "400", "antiforgery_validation_failed", "invalid_invitation");
        AssertProblemCodes(accept, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(accept, "403", "permission_denied");
        AssertProblemCodes(accept, "409", "invitation_conflict");
        AssertProblemCodes(accept, "500", "internal_server_error");
        foreach (var status in new[] { "404", "429" })
        {
            accept.TryGetProperty(status, out _).ShouldBeFalse($"acceptance must not advertise {status}.");
        }
    }

    /// <summary>
    /// The success bodies are endpoint DTOs, not the internal Result and not a universal envelope (IA-REQ-038).
    /// Reading the declared schema is what makes that checkable without issuing a request.
    /// </summary>
    [Test]
    public async Task Invitation_success_schemas_are_endpoint_dtos_and_never_carry_a_token()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var paths = document.GetProperty("paths");

        var created = SchemaOf(document, paths, "/api/tenants/{tenantId}/invitations", "post", "201");
        created.Select(property => property.Name).Order(StringComparer.Ordinal).ShouldBe(["expiresAt", "invitationId"]);

        var acceptance = SchemaOf(document, paths, "/api/invitations/accept", "post", "200");
        acceptance.Select(property => property.Name).Order(StringComparer.Ordinal).ShouldBe(["membershipId", "tenantId"]);

        foreach (var property in created.Concat(acceptance))
        {
            property.Name.ShouldNotContain("token", Case.Insensitive);
            property.Name.ShouldNotContain("secret", Case.Insensitive);
        }
    }

    private static JsonProperty[] SchemaOf(JsonElement document, JsonElement paths, string path, string verb, string status)
    {
        var schema = paths.GetProperty(path).GetProperty(verb).GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            schema = document.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
        }

        return schema.GetProperty("properties").EnumerateObject().ToArray();
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
        AssertProblemCodes(select, "409", "session_concurrency_conflict");
        AssertProblemCodes(select, "500", "internal_server_error");
        foreach (var status in new[] { "404", "429" }) select.TryGetProperty(status, out _).ShouldBeFalse();
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
        // The one non-neutral refusal a sign-in can reach: the credential it validated was replaced before the
        // session could be issued, so no session exists to hand back. A stranger cannot provoke it — moving an
        // identity's security version requires spending that identity's own proof or reset link (C2/C4).
        AssertProblemCodes(create, "401", "credential_superseded");
        AssertProblemCodes(create, "429", "rate_limit_exceeded");
        create.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _).ShouldBeTrue("the login 429 must advertise Retry-After");
        AssertProblemCodes(create, "500", "internal_server_error");
        foreach (var status in new[] { "403", "404", "409" }) create.TryGetProperty(status, out _).ShouldBeFalse($"POST sessions must not advertise {status}.");

        var revoke = paths.GetProperty("/api/identity/sessions/current").GetProperty("delete").GetProperty("responses");
        revoke.TryGetProperty("204", out _).ShouldBeTrue();
        revoke.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(revoke, "400", "antiforgery_validation_failed");
        AssertProblemCodes(revoke, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(revoke, "409", "session_concurrency_conflict");
        AssertProblemCodes(revoke, "500", "internal_server_error");
        foreach (var status in new[] { "403", "404", "429" }) revoke.TryGetProperty(status, out _).ShouldBeFalse($"DELETE sessions/current must not advertise {status}.");
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
