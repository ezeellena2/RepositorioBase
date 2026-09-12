using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class RegistrationHttpValidationTests : TestBase
{
    public static IEnumerable<TestCaseData> BindingFailures()
    {
        yield return new TestCaseData("/organizations/register", "{\"password\":\"binding-password-sentinel\"", "validation_failed", "binding-password-sentinel").SetName("Organization_registration_malformed_json");
        yield return new TestCaseData("/organizations/register", null, "validation_failed", string.Empty).SetName("Organization_registration_absent_body");
        yield return new TestCaseData("/organizations/register", "{\"email\":42,\"password\":\"binding-password-sentinel\",\"legalName\":\"Northwind\",\"cuit\":\"30-12345678-1\"}", "validation_failed", "binding-password-sentinel").SetName("Organization_registration_type_conversion_failure");
        yield return new TestCaseData("/personal/register", "{\"password\":\"binding-password-sentinel\"", "validation_failed", "binding-password-sentinel").SetName("Personal_registration_malformed_json");
        yield return new TestCaseData("/personal/register", null, "validation_failed", string.Empty).SetName("Personal_registration_absent_body");
        yield return new TestCaseData("/personal/register", "{\"email\":42,\"password\":\"binding-password-sentinel\",\"fullName\":\"Ada Lovelace\",\"displayName\":\"Ada\",\"documentNumber\":\"12345678\"}", "validation_failed", "binding-password-sentinel").SetName("Personal_registration_type_conversion_failure");
        yield return new TestCaseData("/confirm-email", "{\"token\":\"binding-token-sentinel\"", "invalid_confirmation", "binding-token-sentinel").SetName("Confirmation_malformed_json");
        yield return new TestCaseData("/confirm-email", null, "invalid_confirmation", string.Empty).SetName("Confirmation_absent_body");
        yield return new TestCaseData("/confirm-email", "{\"token\":42}", "invalid_confirmation", "binding-token-sentinel").SetName("Confirmation_type_conversion_failure");
    }

    public static IEnumerable<TestCaseData> TrailingSlashBindingFailures()
    {
        yield return new TestCaseData("/organizations/register/", "{\"password\":\"trailing-password-sentinel\"", "validation_failed", "trailing-password-sentinel").SetName("Organization_registration_trailing_slash_malformed_json");
        yield return new TestCaseData("/organizations/register/", null, "validation_failed", string.Empty).SetName("Organization_registration_trailing_slash_absent_body");
        yield return new TestCaseData("/personal/register/", "{\"password\":\"trailing-password-sentinel\"", "validation_failed", "trailing-password-sentinel").SetName("Personal_registration_trailing_slash_malformed_json");
        yield return new TestCaseData("/personal/register/", null, "validation_failed", string.Empty).SetName("Personal_registration_trailing_slash_absent_body");
        yield return new TestCaseData("/confirm-email/", "{\"token\":\"trailing-token-sentinel\"", "invalid_confirmation", "trailing-token-sentinel").SetName("Confirmation_trailing_slash_malformed_json");
        yield return new TestCaseData("/confirm-email/", "{\"token\":42}", "invalid_confirmation", "trailing-token-sentinel").SetName("Confirmation_trailing_slash_type_conversion_failure");
    }

    [TestCaseSource(nameof(TrailingSlashBindingFailures))]
    public async Task Valid_antiforgery_identity_posts_with_a_trailing_slash_map_body_binding_failures_to_the_endpoint_contract(string path, string? json, string expectedCode, string forbiddenSecret)
    {
        var host = $"https://binding-trailing-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity{path}");
        if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe(expectedCode);
        text.ShouldNotContain("invalid_request");
        text.ShouldNotContain("JSON", Case.Insensitive);
        if (!string.IsNullOrEmpty(forbiddenSecret)) text.ShouldNotContain(forbiddenSecret);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    [TestCaseSource(nameof(BindingFailures))]
    public async Task Valid_antiforgery_identity_posts_map_body_binding_failures_to_the_endpoint_contract(string path, string? json, string expectedCode, string forbiddenSecret)
    {
        var host = $"https://binding-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity{path}");
        if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe(expectedCode);
        text.ShouldNotContain("invalid_request");
        text.ShouldNotContain("JSON", Case.Insensitive);
        if (!string.IsNullOrEmpty(forbiddenSecret)) text.ShouldNotContain(forbiddenSecret);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    public static IEnumerable<TestCaseData> InvalidPayloads()
    {
        yield return new TestCaseData(
            "/organizations/register",
            new { email = string.Empty, password = string.Empty, legalName = " ", cuit = string.Empty },
            Errors(
                ("email", "Enter an email address."),
                ("password", "A password is required."),
                ("legalName", "A legal name is required."),
                ("cuit", "A CUIT is required.")),
            Array.Empty<string>()).SetName("Organization_required_fields_have_exact_wire_errors");

        var organizationEmail = new string('e', 257);
        var organizationPassword = new string('p', 257);
        var legalName = new string('n', 257);
        var longCuit = new string('1', 33);
        yield return new TestCaseData(
            "/organizations/register",
            new { email = organizationEmail, password = organizationPassword, legalName, cuit = longCuit },
            Errors(
                ("email", "The email address must be 256 characters or fewer."),
                ("password", "The password must be 256 characters or fewer."),
                ("legalName", "The legal name must be 256 characters or fewer."),
                ("cuit", "The CUIT must be 32 characters or fewer.")),
            new[] { organizationEmail, organizationPassword, legalName, longCuit }).SetName("Organization_length_rules_have_exact_wire_errors");

        const string organizationInvalidEmail = "organization-address-sentinel";
        const string invalidCuit = "30/12345678/9";
        yield return new TestCaseData(
            "/organizations/register",
            new { email = organizationInvalidEmail, password = "Testing1234!", legalName = "Northwind", cuit = invalidCuit },
            Errors(
                ("email", "Enter an email address."),
                ("cuit", "The CUIT must contain exactly eleven digits and may use only digits, hyphens, and whitespace.")),
            new[] { organizationInvalidEmail, "Testing1234!", "Northwind", invalidCuit }).SetName("Organization_shape_rules_have_exact_wire_errors");

        const string wrongCheckDigit = "30-12345678-9";
        yield return new TestCaseData(
            "/organizations/register",
            new { email = "owner@example.test", password = "Testing1234!", legalName = "Northwind", cuit = wrongCheckDigit },
            Errors(("cuit", "That CUIT's check digit does not match. Check the number.")),
            new[] { wrongCheckDigit, "12345678" }).SetName("Organization_check_digit_rule_has_exact_wire_error");

        yield return new TestCaseData(
            "/personal/register",
            new { email = string.Empty, password = string.Empty, fullName = " ", displayName = " ", documentNumber = string.Empty },
            Errors(
                ("email", "Enter an email address."),
                ("password", "A password is required."),
                ("fullName", "A full name is required."),
                ("displayName", "A display name is required."),
                ("documentNumber", "A document number is required.")),
            Array.Empty<string>()).SetName("Personal_required_fields_have_exact_wire_errors");

        var personalEmail = new string('e', 257);
        var personalPassword = new string('p', 257);
        var fullName = new string('f', 201);
        var displayName = new string('d', 61);
        var longDocument = new string('1', 33);
        yield return new TestCaseData(
            "/personal/register",
            new { email = personalEmail, password = personalPassword, fullName, displayName, documentNumber = longDocument },
            Errors(
                ("email", "The email address must be 256 characters or fewer."),
                ("password", "The password must be 256 characters or fewer."),
                ("fullName", "The full name must be 200 characters or fewer."),
                ("displayName", "The display name must be 60 characters or fewer."),
                ("documentNumber", "The document number must be 32 characters or fewer.")),
            new[] { personalEmail, personalPassword, fullName, displayName, longDocument }).SetName("Personal_length_rules_have_exact_wire_errors");

        const string personalInvalidEmail = "personal-address-sentinel";
        const string invalidDocument = "12/345";
        yield return new TestCaseData(
            "/personal/register",
            new { email = personalInvalidEmail, password = "Testing1234!", fullName = "Ada Lovelace", displayName = "Ada", documentNumber = invalidDocument },
            Errors(
                ("email", "Enter an email address."),
                ("documentNumber", "An Argentine DNI must contain seven or eight digits and may use only digits, dots, hyphens, and whitespace.")),
            new[] { personalInvalidEmail, "Testing1234!", "Ada Lovelace", "Ada", invalidDocument }).SetName("Personal_shape_rules_have_exact_wire_errors");
    }

    [TestCaseSource(nameof(InvalidPayloads))]
    public async Task Valid_antiforgery_registration_rejects_invalid_input_without_effects_or_secret_echoes(
        string path,
        object payload,
        IReadOnlyDictionary<string, string[]> expectedErrors,
        string[] forbiddenValues)
    {
        var host = $"https://registration-input-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity{path}") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var before = await RegistrationDurableCountsAsync();
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        await AssertValidationProblemAsync(response, $"/api/identity{path}", expectedErrors, forbiddenValues);
        (await RegistrationDurableCountsAsync()).ShouldBe(before);
    }

    [TestCase("/api/identity/organizations/register")]
    [TestCase("/api/identity/personal/register")]
    public async Task Password_policy_refusal_is_identical_for_taken_and_free_addresses_before_lookup(string route)
    {
        var knownEmail = $"known-{Guid.NewGuid():N}@example.test";
        var freeEmail = $"free-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(knownEmail, "Testing1234!");

        var probe = new IdentityLookupProbe();
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            services.RemoveAll<IIdentityAccountService>();
            services.AddScoped<IIdentityAccountService>(provider => new ObservedIdentityAccountService(
                ActivatorUtilities.CreateInstance<IdentityAccountService>(provider),
                probe));
        });
        var host = $"https://registration-parity-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(harness.Client, host);
        var before = await RegistrationDurableCountsAsync();
        string[] expectedPolicyErrors = [
            "Passwords must be at least 12 characters.",
            "Passwords must have at least one non alphanumeric character.",
            "Passwords must have at least one digit ('0'-'9').",
            "Passwords must have at least one uppercase ('A'-'Z')."
        ];
        var expectedErrors = Errors(("password", expectedPolicyErrors));

        using var knownRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}{route}",
            RegistrationPayload(route, knownEmail, "short"),
            antiforgery);
        using var knownResponse = await harness.Client.SendAsync(knownRequest);
        var knownBody = await AssertValidationProblemAsync(
            knownResponse,
            route,
            expectedErrors,
            knownEmail,
            "short");

        using var freeRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}{route}",
            RegistrationPayload(route, freeEmail, "short"),
            antiforgery);
        using var freeResponse = await harness.Client.SendAsync(freeRequest);
        var freeBody = await AssertValidationProblemAsync(
            freeResponse,
            route,
            expectedErrors,
            freeEmail,
            "short");

        StableProblemBody(freeBody).ShouldBe(
            StableProblemBody(knownBody),
            "request-only validation must not reveal whether the submitted address already has an account");
        probe.PasswordValidationCount.ShouldBe(2, "both requests must reach the configured request-only password policy");
        probe.FindByEmailCount.ShouldBe(0, "validation must finish before any account lookup");
        (await RegistrationDurableCountsAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Valid_anonymous_organization_registration_is_the_same_bodyless_202_for_taken_and_free_addresses()
    {
        var knownEmail = $"known-{Guid.NewGuid():N}@example.test";
        var freeEmail = $"free-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(knownEmail, "Testing1234!");
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var host = $"https://registration-neutral-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(harness.Client, host);
        var before = await RegistrationDurableCountsAsync();

        using var knownRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/organizations/register",
            new { email = knownEmail, password = "Testing1234!", legalName = "Known Address", cuit = "30-12345678-1" },
            antiforgery);
        using var knownResponse = await harness.Client.SendAsync(knownRequest);

        using var freeRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/organizations/register",
            new { email = freeEmail, password = "Testing1234!", legalName = "Free Address", cuit = "30-87654321-0" },
            antiforgery);
        using var freeResponse = await harness.Client.SendAsync(freeRequest);

        knownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        freeResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await knownResponse.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await freeResponse.Content.ReadAsStringAsync()).ShouldBeEmpty();

        var after = await RegistrationDurableCountsAsync();
        after.Identities.ShouldBe(before.Identities);
        after.Submissions.ShouldBe(before.Submissions + 2);
        after.OrganizationIntents.ShouldBe(before.OrganizationIntents + 2);
        after.Tenants.ShouldBe(before.Tenants);
        after.Organizations.ShouldBe(before.Organizations);
        after.Memberships.ShouldBe(before.Memberships);
        after.OutboxMessages.ShouldBe(before.OutboxMessages + 2);
        after.OutboxSecrets.ShouldBe(before.OutboxSecrets + 1);
    }

    [Test]
    public async Task Cuit_check_digit_refusal_is_identical_for_taken_and_free_values_before_lookup()
    {
        const string takenCuit = "30-12345678-9";
        const string freeCuit = "99-12345678-9";
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"legacy-cuit-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(OrganizationProfile.Create(
            tenant,
            "Legacy Organization",
            NormalizedCuit.FromStored("30123456789")));

        var probe = new IdentityLookupProbe();
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            services.RemoveAll<IIdentityAccountService>();
            services.AddScoped<IIdentityAccountService>(provider => new ObservedIdentityAccountService(
                ActivatorUtilities.CreateInstance<IdentityAccountService>(provider),
                probe));
        });
        var host = $"https://registration-cuit-parity-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(harness.Client, host);
        var before = await RegistrationDurableCountsAsync();
        var expectedErrors = Errors(("cuit", "That CUIT's check digit does not match. Check the number."));

        using var takenRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/organizations/register",
            new { email = "taken-probe@example.test", password = "Testing1234!", legalName = "Probe", cuit = takenCuit },
            antiforgery);
        using var takenResponse = await harness.Client.SendAsync(takenRequest);
        var takenBody = await AssertValidationProblemAsync(
            takenResponse,
            "/api/identity/organizations/register",
            expectedErrors,
            takenCuit,
            "12345678");

        using var freeRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/organizations/register",
            new { email = "free-probe@example.test", password = "Testing1234!", legalName = "Probe", cuit = freeCuit },
            antiforgery);
        using var freeResponse = await harness.Client.SendAsync(freeRequest);
        var freeBody = await AssertValidationProblemAsync(
            freeResponse,
            "/api/identity/organizations/register",
            expectedErrors,
            freeCuit,
            "12345678");

        StableProblemBody(freeBody).ShouldBe(
            StableProblemBody(takenBody),
            "a request-only check-digit refusal must not reveal whether those stored digits already exist");
        probe.PasswordValidationCount.ShouldBe(0, "command validation must finish before the handler's first operation");
        probe.FindByEmailCount.ShouldBe(0, "validation must finish before any account lookup");
        (await RegistrationDurableCountsAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Valid_antiforgery_registration_exposes_invalid_session_as_401()
    {
        TestApp.SetValidatedOptionalSession(null, null, isInvalid: true);
        var host = $"https://registration-session-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/organizations/register")
        {
            Content = JsonContent.Create(new { email = "owner@example.test", password = "Testing1234!", legalName = "Northwind", cuit = "30-12345678-1" })
        };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().ShouldBe("invalid_session");
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
    }

    private static IReadOnlyDictionary<string, string[]> Errors(params (string Field, string Message)[] errors) =>
        errors.ToDictionary(error => error.Field, error => new[] { error.Message }, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string[]> Errors(params (string Field, string[] Messages)[] errors) =>
        errors.ToDictionary(error => error.Field, error => error.Messages, StringComparer.Ordinal);

    private static object RegistrationPayload(string route, string email, string password) =>
        route.EndsWith("/personal/register", StringComparison.Ordinal)
            ? new { email, password, fullName = "Ada Lovelace", displayName = "Ada", documentNumber = "12345678" }
            : new { email, password, legalName = "Northwind", cuit = "30-12345678-1" };

    private static async Task<string> AssertValidationProblemAsync(
        HttpResponseMessage response,
        string instance,
        IReadOnlyDictionary<string, string[]> expectedErrors,
        params string[] forbiddenValues)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        var problem = document.RootElement;
        problem.GetProperty("status").GetInt32().ShouldBe(400);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
        problem.GetProperty("title").GetString().ShouldBe("Bad Request");
        problem.GetProperty("instance").GetString().ShouldBe(instance);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(expectedErrors.Keys.OrderBy(name => name, StringComparer.Ordinal));
        foreach (var expected in expectedErrors)
        {
            errors.GetProperty(expected.Key).EnumerateArray().Select(message => message.GetString())
                .ShouldBe(expected.Value);
        }

        foreach (var value in forbiddenValues.Where(value => !string.IsNullOrEmpty(value)))
        {
            raw.ShouldNotContain(value);
        }

        return raw;
    }

    private static string StableProblemBody(string raw)
    {
        var problem = JsonNode.Parse(raw)!.AsObject();
        problem.Remove("traceId");
        problem.Remove("instance");
        return problem.ToJsonString();
    }

    private static async Task<RegistrationDurableCounts> RegistrationDurableCountsAsync() => new(
        await TestApp.CountAsync<ApplicationUser>(),
        await TestApp.CountAsync<RegistrationSubmission>(),
        await TestApp.CountAsync<PendingRegistrationIntent>(),
        await TestApp.CountAsync<PendingPersonalIntent>(),
        await TestApp.CountAsync<Tenant>(),
        await TestApp.CountAsync<OrganizationProfile>(),
        await TestApp.CountAsync<PersonProfile>(),
        await TestApp.CountAsync<IdentityDocument>(),
        await TestApp.CountAsync<IdentityDocumentFingerprint>(),
        await TestApp.CountAsync<TenantMembership>(),
        await TestApp.CountAsync<OutboxMessage>(),
        await TestApp.CountAsync<OutboxSecret>(),
        await TestApp.CountAsync<AuditEvent>());

    private static async Task<string> BootstrapAsync(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
    }

    private sealed record RegistrationDurableCounts(
        int Identities,
        int Submissions,
        int OrganizationIntents,
        int PersonalIntents,
        int Tenants,
        int Organizations,
        int People,
        int Documents,
        int DocumentFingerprints,
        int Memberships,
        int OutboxMessages,
        int OutboxSecrets,
        int AuditEvents);

    private sealed class IdentityLookupProbe
    {
        private int _findByEmailCount;
        private int _passwordValidationCount;

        public int FindByEmailCount => Volatile.Read(ref _findByEmailCount);
        public int PasswordValidationCount => Volatile.Read(ref _passwordValidationCount);

        public void RecordFindByEmail() => Interlocked.Increment(ref _findByEmailCount);
        public void RecordPasswordValidation() => Interlocked.Increment(ref _passwordValidationCount);
    }

    private sealed class ObservedIdentityAccountService(
        IIdentityAccountService inner,
        IdentityLookupProbe probe) : IIdentityAccountService
    {
        public Task<IdentityAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            probe.RecordFindByEmail();
            return inner.FindByEmailAsync(normalizedEmail, cancellationToken);
        }

        public Task<IdentityAccount?> FindByIdAsync(Guid identityId, CancellationToken cancellationToken) =>
            inner.FindByIdAsync(identityId, cancellationToken);

        public Task<IdentityAccount?> ValidateCredentialsAsync(
            string normalizedEmail,
            string password,
            CancellationToken cancellationToken) =>
            inner.ValidateCredentialsAsync(normalizedEmail, password, cancellationToken);

        public Task<IdentityAccountValidationResult> ValidatePendingRegistrationAsync(
            string normalizedEmail,
            string password,
            CancellationToken cancellationToken) =>
            inner.ValidatePendingRegistrationAsync(normalizedEmail, password, cancellationToken);

        public Task<IdentityAccountValidationResult> ValidatePasswordAsync(
            string password,
            CancellationToken cancellationToken)
        {
            probe.RecordPasswordValidation();
            return inner.ValidatePasswordAsync(password, cancellationToken);
        }

        public Task<IdentityAccountCreationResult> CreatePendingAsync(
            string normalizedEmail,
            string password,
            CancellationToken cancellationToken) =>
            inner.CreatePendingAsync(normalizedEmail, password, cancellationToken);

        public string HashPassword(string password) => inner.HashPassword(password);

        public Task<IdentityAccountCreationResult> CreatePendingFromHashAsync(
            string normalizedEmail,
            string passwordHash,
            CancellationToken cancellationToken) =>
            inner.CreatePendingFromHashAsync(normalizedEmail, passwordHash, cancellationToken);

        public Task ActivateAsync(Guid identityId, CancellationToken cancellationToken) =>
            inner.ActivateAsync(identityId, cancellationToken);

        public Task<bool> VerifyPasswordAsync(Guid identityId, string password, CancellationToken cancellationToken) =>
            inner.VerifyPasswordAsync(identityId, password, cancellationToken);

        public Task<bool> TryTransitionAsync(
            Guid identityId,
            CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus expected,
            CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus next,
            CancellationToken cancellationToken) =>
            inner.TryTransitionAsync(identityId, expected, next, cancellationToken);
    }
}
