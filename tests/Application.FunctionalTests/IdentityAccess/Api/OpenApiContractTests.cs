using System.Text.Json;
using System.Net;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class OpenApiContractTests : TestBase
{
    [Test]
    public async Task Swagger_UI_is_available_in_development()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/swagger/index.html");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Swagger UI");

        var initializer = await FunctionalTestSetup.HttpClient.GetStringAsync("/swagger/index.js");
        initializer.ShouldContain("/openapi/v1.json");
    }

    [Test]
    public async Task Swagger_prefix_reaches_the_development_UI_before_the_SPA_fallback()
    {
        using var response = await FunctionalTestSetup.HttpClient.GetAsync("/swagger");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/swagger/index.html");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Swagger UI");
        page.ShouldNotContain("functional-spa-index");
    }

    [Test]
    public async Task Swagger_UI_is_not_exposed_in_production()
    {
        using var factory = new WebApiFactory(
            FunctionalTestSetup.ConnectionString,
            Microsoft.Extensions.Hosting.Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");
        var page = await response.Content.ReadAsStringAsync();

        page.ShouldNotContain("Swagger UI");

        var initializer = await client.GetAsync("/swagger/index.js");
        initializer.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

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
        AssertProblemCodes(register, "400", "antiforgery_validation_failed", "validation_failed", "invalid_registration");
        AssertProblemCodes(register, "401", "invalid_session");
        // Still declared, and still produced — but now only for an authenticated caller whose own CUIT is already
        // registered. An anonymous caller is answered neutrally in that case, so that a taken address and an
        // untaken one cannot be told apart (IA-REQ-003). Do not delete this as a dead declaration.
        AssertProblemCodes(register, "409", "registration_conflict");
        AssertProblemCodes(register, "500", "internal_server_error");

        var personalRegister = paths.GetProperty("/api/identity/personal/register").GetProperty("post").GetProperty("responses");
        personalRegister.TryGetProperty("202", out _).ShouldBeTrue();
        personalRegister.GetProperty("202").TryGetProperty("content", out _).ShouldBeFalse();
        foreach (var status in new[] { "400", "401", "409", "500" })
        {
            personalRegister.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        }
        AssertProblemCodes(personalRegister, "400", "antiforgery_validation_failed", "validation_failed", "invalid_registration");
        AssertProblemCodes(personalRegister, "401", "invalid_session");
        AssertProblemCodes(personalRegister, "409", "personal_registration_conflict");
        AssertProblemCodes(personalRegister, "500", "internal_server_error");
        foreach (var status in new[] { "429", "503" })
        {
            personalRegister.TryGetProperty(status, out _).ShouldBeFalse("registration itself spends no attempt budget");
        }

        var confirm = paths.GetProperty("/api/identity/confirm-email").GetProperty("post").GetProperty("responses");
        confirm.TryGetProperty("204", out _).ShouldBeTrue();
        confirm.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(confirm, "400", "antiforgery_validation_failed", "invalid_confirmation");
        AssertProblemCodes(confirm, "409", "registration_conflict", "personal_registration_conflict");
        AssertProblemCodes(confirm, "429", "rate_limit_exceeded");
        AssertProblemCodes(confirm, "503", "service_unavailable");
        confirm.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _).ShouldBeTrue();
        confirm.GetProperty("503").GetProperty("headers").TryGetProperty("Retry-After", out _).ShouldBeTrue();
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

        foreach (var route in new[] { "/api/platform/mfa/enroll", "/api/platform/mfa/recovery-acknowledge" })
        {
            var gate = paths.GetProperty(route).GetProperty("post").GetProperty("responses");
            AssertProblemCodes(gate, "400", "antiforgery_validation_failed", "validation_failed", "invalid_invitation");
            AssertProblemCodes(gate, "401", "authentication_required", "invalid_session");
            AssertProblemCodes(gate, "403", "permission_denied");
            AssertProblemCodes(gate, "409", "invitation_conflict");
            AssertProblemCodes(gate, "500", "internal_server_error");
            foreach (var status in new[] { "404", "429", "503" })
            {
                gate.TryGetProperty(status, out _).ShouldBeFalse($"{route} must not advertise {status}.");
            }
        }

        var verify = paths.GetProperty("/api/platform/mfa/verify").GetProperty("post").GetProperty("responses");
        AssertProblemCodes(verify, "400", "antiforgery_validation_failed", "validation_failed", "invalid_invitation", "invalid_mfa_code");
        AssertProblemCodes(verify, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(verify, "403", "permission_denied");
        AssertProblemCodes(verify, "429", "rate_limit_exceeded");
        AssertProblemCodes(verify, "500", "internal_server_error");
        AssertProblemCodes(verify, "503", "service_unavailable");
        foreach (var status in new[] { "404", "409" }) verify.TryGetProperty(status, out _).ShouldBeFalse();

        var stepUp = paths.GetProperty("/api/platform/mfa/step-up").GetProperty("post").GetProperty("responses");
        AssertProblemCodes(stepUp, "400", "antiforgery_validation_failed", "validation_failed", "invalid_mfa_code", "invalid_invitation");
        AssertProblemCodes(stepUp, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(stepUp, "403", "permission_denied");
        AssertProblemCodes(stepUp, "429", "rate_limit_exceeded");
        AssertProblemCodes(stepUp, "500", "internal_server_error");
        AssertProblemCodes(stepUp, "503", "service_unavailable");
        foreach (var status in new[] { "404", "409" }) stepUp.TryGetProperty(status, out _).ShouldBeFalse();

        var recovery = paths.GetProperty("/api/platform/mfa/recover").GetProperty("post").GetProperty("responses");
        AssertProblemCodes(recovery, "400", "antiforgery_validation_failed", "validation_failed", "invalid_recovery_code", "invalid_credential_proof");
        AssertProblemCodes(recovery, "401", "authentication_required", "invalid_session", "recent_proof_required");
        AssertProblemCodes(recovery, "403", "permission_denied");
        AssertProblemCodes(recovery, "409", "platform_mfa_concurrency_conflict");
        AssertProblemCodes(recovery, "429", "rate_limit_exceeded");
        AssertProblemCodes(recovery, "500", "internal_server_error");
        AssertProblemCodes(recovery, "503", "service_unavailable");
        recovery.TryGetProperty("404", out _).ShouldBeFalse();

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
        AssertProblemCodes(issue, "400", "antiforgery_validation_failed", "validation_failed", "invalid_invitation");
        AssertProblemCodes(issue, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(issue, "403", "permission_denied");
        AssertProblemCodes(issue, "409", "invitation_conflict");
        AssertProblemCodes(issue, "500", "internal_server_error");
        issue.TryGetProperty("404", out _).ShouldBeFalse("the invitation boundary and handler cannot produce not_found");
        issue.TryGetProperty("429", out _).ShouldBeFalse("issuing is not rate limited in this increment");
        issue.TryGetProperty("202", out _).ShouldBeFalse();

        // Registration is the neutral half: bodyless, and it advertises no status that would let a caller tell a
        // live token from a dead one (IA-REQ-016, SPEC section 6).
        var register = paths.GetProperty("/api/invitations/register").GetProperty("post").GetProperty("responses");
        register.TryGetProperty("202", out _).ShouldBeTrue();
        register.GetProperty("202").TryGetProperty("content", out _).ShouldBeFalse("the neutral 202 carries no body");
        AssertProblemCodes(register, "400", "antiforgery_validation_failed", "validation_failed");
        AssertProblemCodes(register, "500", "internal_server_error");
        foreach (var status in new[] { "401", "403", "404", "409", "429" })
        {
            register.TryGetProperty(status, out _).ShouldBeFalse($"the public registration route must not advertise {status}.");
        }

        var platformRegister = paths.GetProperty("/api/platform/invitations/register").GetProperty("post").GetProperty("responses");
        platformRegister.TryGetProperty("202", out _).ShouldBeTrue();
        platformRegister.GetProperty("202").TryGetProperty("content", out _).ShouldBeFalse("the neutral 202 carries no body");
        AssertProblemCodes(platformRegister, "400", "antiforgery_validation_failed", "validation_failed");
        AssertProblemCodes(platformRegister, "500", "internal_server_error");
        foreach (var status in new[] { "401", "403", "404", "409", "429" })
        {
            platformRegister.TryGetProperty(status, out _).ShouldBeFalse($"the public Platform registration route must not advertise {status}.");
        }

        var platformConfirm = paths.GetProperty("/api/platform/invitations/confirm").GetProperty("post").GetProperty("responses");
        platformConfirm.TryGetProperty("204", out _).ShouldBeTrue();
        platformConfirm.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse("confirmation success is bodyless");
        AssertProblemCodes(platformConfirm, "400", "antiforgery_validation_failed", "validation_failed", "invalid_confirmation");
        AssertProblemCodes(platformConfirm, "500", "internal_server_error");
        foreach (var status in new[] { "401", "403", "404", "409", "429", "503" })
        {
            platformConfirm.TryGetProperty(status, out _).ShouldBeFalse($"Platform confirmation must not advertise {status}.");
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
        AssertProblemCodes(context, "403", "permission_denied");
        AssertProblemCodes(context, "500", "internal_server_error");
        foreach (var status in new[] { "400", "404", "409", "429" }) context.TryGetProperty(status, out _).ShouldBeFalse();

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

        foreach (var route in new[]
                 {
                     "/api/identity/sessions",
                     "/api/identity/credentials/password/recovery",
                     "/api/identity/account/reactivation-requests"
                 })
        {
            var responses = paths.GetProperty(route).GetProperty("post").GetProperty("responses");
            AssertProblemCodes(responses, "400", "antiforgery_validation_failed", "invalid_request");
        }

        var revoke = paths.GetProperty("/api/identity/sessions/current").GetProperty("delete").GetProperty("responses");
        revoke.TryGetProperty("204", out _).ShouldBeTrue();
        revoke.GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        AssertProblemCodes(revoke, "400", "antiforgery_validation_failed");
        AssertProblemCodes(revoke, "401", "authentication_required", "invalid_session");
        AssertProblemCodes(revoke, "403", "permission_denied");
        AssertProblemCodes(revoke, "409", "session_concurrency_conflict");
        AssertProblemCodes(revoke, "500", "internal_server_error");
        foreach (var status in new[] { "404", "429" }) revoke.TryGetProperty(status, out _).ShouldBeFalse($"DELETE sessions/current must not advertise {status}.");
    }

    [Test]
    public async Task Personal_role_and_membership_operations_declare_the_exact_validation_and_read_absence_codes()
    {
        using var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        var createPersonal = paths.GetProperty("/api/identity/personal").GetProperty("post").GetProperty("responses");
        AssertProblemCodes(createPersonal, "400", "antiforgery_validation_failed", "validation_failed");
        createPersonal.GetProperty("400").GetProperty("x-problem-codes").EnumerateArray()
            .Select(code => code.GetString()).ShouldNotContain("invalid_registration");

        var updateProfile = paths.GetProperty("/api/identity/profile").GetProperty("put").GetProperty("responses");
        AssertProblemCodes(updateProfile, "400", "antiforgery_validation_failed", "validation_failed", "profile_field_not_editable");
        AssertProblemCodes(updateProfile, "409", "personal_profile_concurrency_conflict");

        foreach (var route in new[]
                 {
                     "/api/tenants/{tenantId}/permission-catalog",
                     "/api/tenants/{tenantId}/roles",
                     "/api/tenants/{tenantId}/roles/{roleId}"
                 })
        {
            var read = paths.GetProperty(route).GetProperty("get").GetProperty("responses");
            AssertProblemCodes(read, "404", "not_found");
        }

        foreach (var route in new[]
                 {
                     "/api/tenants/{tenantId}/members",
                     "/api/tenants/{tenantId}/invitations"
                 })
        {
            var read = paths.GetProperty(route).GetProperty("get").GetProperty("responses");
            AssertProblemCodes(read, "404", "not_found");
        }

        var createRole = paths.GetProperty("/api/tenants/{tenantId}/roles").GetProperty("post").GetProperty("responses");
        AssertProblemCodes(createRole, "400", "antiforgery_validation_failed", "validation_failed", "invalid_role_operation");
        AssertProblemCodes(createRole, "401", "authentication_required", "invalid_session", "recent_proof_required");
        AssertProblemCodes(createRole, "403", "permission_denied");
        AssertProblemCodes(createRole, "409", "role_concurrency_conflict");
        AssertProblemCodes(createRole, "500", "internal_server_error");
        createRole.TryGetProperty("404", out _).ShouldBeFalse();
        var updateRole = paths.GetProperty("/api/tenants/{tenantId}/roles/{roleId}").GetProperty("put").GetProperty("responses");
        AssertProblemCodes(updateRole, "400", "antiforgery_validation_failed", "validation_failed", "invalid_role_operation");

        var membershipWrite = paths.GetProperty("/api/tenants/{tenantId}/members/{membershipId}/roles")
            .GetProperty("put").GetProperty("responses");
        AssertProblemCodes(membershipWrite, "400", "antiforgery_validation_failed", "invalid_membership_operation");
    }

    [Test]
    public async Task Public_optional_session_routes_declare_their_invalid_session_refusal()
    {
        using var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in new[]
                 {
                     "/api/identity/credentials/password/recovery",
                     "/api/identity/credentials/password/reset",
                     "/api/identity/account/reactivation-requests",
                     "/api/identity/account/reactivate"
                 })
        {
            AssertProblemCodes(
                paths.GetProperty(route).GetProperty("post").GetProperty("responses"),
                "401",
                "invalid_session");
        }
    }

    [Test]
    public async Task Confirmed_email_requirement_is_declared_as_problem_details_on_both_producer_routes()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var paths = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");

        var reauthenticate = paths.GetProperty("/api/identity/credentials/reauthenticate")
            .GetProperty("post").GetProperty("responses");
        AssertProblemCodes(reauthenticate, "403", "permission_denied", "email_confirmation_required");

        var externalLink = paths.GetProperty("/api/identity/external/{provider}/link/start")
            .GetProperty("post").GetProperty("responses");
        AssertProblemCodes(externalLink, "403", "permission_denied", "email_confirmation_required");
    }

    [Test]
    public async Task Served_problem_codes_and_checked_in_client_catalogue_are_the_same_contract()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var declared = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["recovery_admission_closed"] = StatusCodes.Status503ServiceUnavailable
        };

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            if (operation.Value.ValueKind != JsonValueKind.Object
                || !operation.Value.TryGetProperty("responses", out var responses))
            {
                continue;
            }

            foreach (var candidate in responses.EnumerateObject())
            {
                if (!int.TryParse(candidate.Name, out var status)
                    || !candidate.Value.TryGetProperty("x-problem-codes", out var codes))
                {
                    continue;
                }

                foreach (var element in codes.EnumerateArray())
                {
                    var code = element.GetString()!;
                    code.ShouldNotBe(
                        "recovery_admission_closed",
                        $"{operation.Name.ToUpperInvariant()} {path.Name} cannot declare a pre-routing refusal");
                    if (declared.TryGetValue(code, out var existing))
                    {
                        existing.ShouldBe(status, $"{code} must have one status in every endpoint contract");
                    }
                    else
                    {
                        declared.Add(code, status);
                    }
                }
            }
        }

        using var catalogueDocument = JsonDocument.Parse(
            File.ReadAllText(RepositoryFile("src/Web/ClientApp/src/features/identity/problemCodes.json")));
        catalogueDocument.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        var properties = catalogueDocument.RootElement.EnumerateObject().ToArray();
        var catalogueCodes = properties.Select(property => property.Name).ToArray();
        catalogueCodes.Length.ShouldBe(53, "the server catalogue includes the one pre-routing recovery refusal");
        catalogueCodes.SequenceEqual(catalogueCodes.Order(StringComparer.Ordinal)).ShouldBeTrue(
            "the checked-in catalogue must remain deterministic and human-reviewable");
        catalogueCodes.Distinct(StringComparer.Ordinal).Count().ShouldBe(catalogueCodes.Length,
            "the checked-in catalogue cannot declare a code twice");

        var catalogue = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            property.Value.ValueKind.ShouldBe(JsonValueKind.Number,
                $"{property.Name} must declare its integer HTTP status");
            property.Value.TryGetInt32(out var status).ShouldBeTrue(
                $"{property.Name} must declare its integer HTTP status");
            catalogue.Add(property.Name, status);
        }

        catalogue.Keys.ShouldBe(declared.Keys, "the client must neither miss nor invent a server problem code");
        foreach (var (code, status) in declared)
        {
            catalogue[code].ShouldBe(status, $"{code} must keep the status declared by the served OpenAPI document");
        }
        declared.Count.ShouldBe(53, "the served union includes route contracts plus the pre-routing recovery refusal");
    }

    [TestCase(false, HttpStatusCode.TooManyRequests, "rate_limit_exceeded")]
    [TestCase(true, HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task Confirm_email_personal_budget_refusals_are_emitted_only_as_declared(
        bool storeUnavailable,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var email = $"confirm-budget-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new RegisterPersonalCommand(
            email,
            "Different1234!",
            "Contract Person",
            "Contract",
            "12345678"))).IsSuccess.ShouldBeTrue();
        var confirmationToken = TestApp.GetRegistrationRawToken();

        if (storeUnavailable)
        {
            TestApp.ForceAttemptBudgetUnavailable();
        }
        else
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var attempts = scope.ServiceProvider.GetRequiredService<ISharedAttemptBudget>();
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var decision = await attempts.SpendAsync(
                    PersonalAttemptBudgets.DocumentClaim,
                    email,
                    CancellationToken.None);
                decision.Outcome.ShouldBe(AttemptBudgetOutcome.Admitted, $"setup attempt {attempt} must fill, not exceed, the budget");
            }
        }

        var host = $"https://confirm-budget-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/confirm-email",
            new { token = confirmationToken },
            antiforgery);
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var problem = await IdentityHttpHarness.AssertProblemAsync(response, expectedStatus, expectedCode);
        response.Headers.RetryAfter.ShouldNotBeNull();
        await AssertEmittedCodeIsDeclaredAsync("/api/identity/confirm-email", "post", problem);
    }

    [Test]
    public async Task Every_numeric_limit_binding_refusal_is_emitted_only_as_declared()
    {
        await TestApp.RunAsDefaultUserAsync();
        var tenantId = Guid.NewGuid();
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"/api/tenants/{tenantId}/roles"] = "/api/tenants/{tenantId}/roles",
            [$"/api/tenants/{tenantId}/members"] = "/api/tenants/{tenantId}/members",
            [$"/api/tenants/{tenantId}/invitations"] = "/api/tenants/{tenantId}/invitations",
            ["/api/platform/organizations"] = "/api/platform/organizations",
            ["/api/platform/identities"] = "/api/platform/identities",
            ["/api/platform/admins"] = "/api/platform/admins",
            ["/api/platform/audit"] = "/api/platform/audit"
        };

        foreach (var (requestRoute, openApiRoute) in routes)
        {
            using var response = await FunctionalTestSetup.HttpClient.GetAsync($"{requestRoute}?limit=not-a-number");
            var problem = await IdentityHttpHarness.AssertProblemAsync(
                response,
                HttpStatusCode.BadRequest,
                "invalid_request");
            await AssertEmittedCodeIsDeclaredAsync(openApiRoute, "get", problem);
        }
    }

    [Test]
    public async Task Every_api_route_declares_the_failure_codes_its_shared_pipeline_can_emit()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var endpoints = scope.ServiceProvider.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToArray();
        using var openApi = JsonDocument.Parse(
            await FunctionalTestSetup.HttpClient.GetStringAsync("/openapi/v1.json"));
        var paths = openApi.RootElement.GetProperty("paths");

        endpoints.ShouldNotBeEmpty();
        endpoints
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => new
                {
                    Method = method,
                    Route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "unnamed endpoint",
                    Neutral = endpoint.Metadata.GetMetadata<ApiNeutralBodyBindingFailureMetadata>()
                }))
            .Where(candidate => candidate.Neutral is not null)
            .Select(candidate => $"{candidate.Method} {candidate.Route} => {candidate.Neutral!.StatusCode}")
            .Order(StringComparer.Ordinal)
            .ShouldBe(new[]
            {
                "POST /api/identity/account/reactivation-requests => 202",
                "POST /api/identity/credentials/password/recovery => 202",
                "POST /api/identity/sessions => 204"
            });

        endpoints
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => new
                {
                    Method = method,
                    Route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "unnamed endpoint",
                    RejectsInvalidOptionalSession = endpoint.Metadata.GetMetadata<ApiInvalidOptionalSessionMetadata>() is not null
                }))
            .Where(candidate => candidate.RejectsInvalidOptionalSession)
            .Select(candidate => $"{candidate.Method} {candidate.Route}")
            .Order(StringComparer.Ordinal)
            .ShouldBe(new[]
            {
                "POST /api/identity/account/reactivate",
                "POST /api/identity/account/reactivation-requests",
                "POST /api/identity/credentials/password/recovery",
                "POST /api/identity/credentials/password/reset",
                "POST /api/identity/external/complete",
                "POST /api/identity/external/{provider}/login/start",
                "POST /api/identity/organizations/register",
                "POST /api/identity/personal/register"
            });

        foreach (var endpoint in endpoints)
        {
            var contracts = endpoint.Metadata.GetOrderedMetadata<ApiProblemContractMetadata>()
                .SelectMany(metadata => metadata.Contracts)
                .ToArray();
            var route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "unnamed endpoint";

            if (endpoint.Metadata.GetMetadata<ApiBodyBindingFailureMetadata>() is { } binding)
            {
                ShouldDeclare(contracts, StatusCodes.Status400BadRequest, binding.Code, route);
            }

            if (endpoint.Metadata.GetMetadata<ApiNeutralBodyBindingFailureMetadata>() is not null)
            {
                endpoint.Metadata.GetMetadata<ApiBodyBindingFailureMetadata>().ShouldBeNull(
                    $"{route} must choose either a typed 400 binding refusal or a neutral success, never both");
                ShouldDeclare(
                    contracts,
                    StatusCodes.Status400BadRequest,
                    "invalid_request",
                    $"{route} keeps its existing oversized-body transport refusal distinct from neutral parsing");
            }

            if (endpoint.Metadata.GetMetadata<ApiInvalidOptionalSessionMetadata>() is not null)
            {
                ShouldDeclare(contracts, StatusCodes.Status401Unauthorized, "invalid_session", route);
            }

            if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                ShouldDeclare(contracts, StatusCodes.Status401Unauthorized, "authentication_required", route);
                ShouldDeclare(contracts, StatusCodes.Status401Unauthorized, "invalid_session", route);
                ShouldDeclare(contracts, StatusCodes.Status403Forbidden, "permission_denied", route);
            }

            if (endpoint.Metadata.GetMetadata<LoginAttemptBudgetMetadata>() is not null)
            {
                ShouldDeclare(contracts, StatusCodes.Status429TooManyRequests, "rate_limit_exceeded", route);
                ShouldDeclare(contracts, StatusCodes.Status503ServiceUnavailable, "service_unavailable", route);
            }

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            foreach (var contract in contracts.DistinctBy(contract => (contract.StatusCode, contract.Code)))
            {
                var openApiRoute = Regex.Replace(route, ":[^}]+", string.Empty);
                var operation = paths.GetProperty(openApiRoute).GetProperty(method.ToLowerInvariant());
                var response = operation.GetProperty("responses")
                    .GetProperty(contract.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                response.GetProperty("x-problem-codes").EnumerateArray()
                    .Select(element => element.GetString())
                    .ShouldContain(contract.Code, $"{method} {route} must publish its metadata in the served OpenAPI contract");
            }
        }
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CleanArchitecture.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            relativePath);
    }

    private static void AssertProblemCodes(JsonElement responses, string status, params string[] expectedCodes)
    {
        var response = responses.GetProperty(status);
        response.GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
        var codes = response.GetProperty("x-problem-codes").EnumerateArray().Select(element => element.GetString()).ToArray();
        codes.ShouldBe(expectedCodes);
    }

    private static async Task AssertEmittedCodeIsDeclaredAsync(string route, string method, JsonElement problem)
    {
        using var openApi = JsonDocument.Parse(
            await FunctionalTestSetup.HttpClient.GetStringAsync("/openapi/v1.json"));
        var code = problem.GetProperty("code").GetString();
        var status = problem.GetProperty("status").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var declared = openApi.RootElement.GetProperty("paths")
            .GetProperty(route)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(status)
            .GetProperty("x-problem-codes")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        declared.ShouldContain(code, $"{method.ToUpperInvariant()} {route} emitted {status} {code}");
    }

    private static void ShouldDeclare(
        IEnumerable<ApiProblemContract> contracts,
        int status,
        string code,
        string route) =>
        contracts.ShouldContain(
            contract => contract.StatusCode == status && string.Equals(contract.Code, code, StringComparison.Ordinal),
            $"{route} can emit {status} {code} from its shared HTTP pipeline");
}
