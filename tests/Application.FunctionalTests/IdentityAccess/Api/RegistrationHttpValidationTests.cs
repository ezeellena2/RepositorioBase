using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class RegistrationHttpValidationTests : TestBase
{
    public static IEnumerable<TestCaseData> BindingFailures()
    {
        yield return new TestCaseData("/organizations/register", "{\"password\":\"binding-password-sentinel\"", "invalid_registration", "binding-password-sentinel").SetName("Registration_malformed_json");
        yield return new TestCaseData("/organizations/register", null, "invalid_registration", string.Empty).SetName("Registration_absent_body");
        yield return new TestCaseData("/organizations/register", "{\"email\":42,\"password\":\"binding-password-sentinel\",\"legalName\":\"Northwind\",\"cuit\":\"30-12345678-9\"}", "invalid_registration", "binding-password-sentinel").SetName("Registration_type_conversion_failure");
        yield return new TestCaseData("/confirm-email", "{\"token\":\"binding-token-sentinel\"", "invalid_confirmation", "binding-token-sentinel").SetName("Confirmation_malformed_json");
        yield return new TestCaseData("/confirm-email", null, "invalid_confirmation", string.Empty).SetName("Confirmation_absent_body");
        yield return new TestCaseData("/confirm-email", "{\"token\":42}", "invalid_confirmation", "binding-token-sentinel").SetName("Confirmation_type_conversion_failure");
    }

    public static IEnumerable<TestCaseData> TrailingSlashBindingFailures()
    {
        yield return new TestCaseData("/organizations/register/", "{\"password\":\"trailing-password-sentinel\"", "invalid_registration", "trailing-password-sentinel").SetName("Registration_trailing_slash_malformed_json");
        yield return new TestCaseData("/organizations/register/", null, "invalid_registration", string.Empty).SetName("Registration_trailing_slash_absent_body");
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
        yield return new TestCaseData(new { email = (string?)null, password = "Testing1234!", legalName = "Northwind", cuit = "30-12345678-9" }).SetName("Null_email");
        yield return new TestCaseData(new { email = "not-an-email", password = "Testing1234!", legalName = "Northwind", cuit = "30-12345678-9" }).SetName("Malformed_email");
        yield return new TestCaseData(new { email = "owner@example.test", password = "weak", legalName = "Northwind", cuit = "30-12345678-9" }).SetName("Weak_password");
        yield return new TestCaseData(new { email = "owner@example.test", password = "Testing1234!", legalName = "Northwind", cuit = "bad-cuit" }).SetName("Malformed_cuit");
        yield return new TestCaseData(new { email = $"{new string('a', 320)}@example.test", password = "Testing1234!", legalName = "Northwind", cuit = "30-12345678-9" }).SetName("Oversized_email");
        yield return new TestCaseData(new { email = "owner@example.test", password = "Testing1234!", legalName = new string('N', 512), cuit = "30-12345678-9" }).SetName("Oversized_legal_name");
    }

    [TestCaseSource(nameof(InvalidPayloads))]
    public async Task Valid_antiforgery_registration_rejects_invalid_input_without_effects_or_secret_echoes(object payload)
    {
        var host = $"https://registration-input-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/organizations/register") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe("invalid_registration");
        text.ShouldNotContain("Testing1234!");
        text.ShouldNotContain("weak");
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    [Test]
    public async Task Valid_antiforgery_registration_exposes_invalid_session_as_401()
    {
        TestApp.SetValidatedOptionalSession(null, null, isInvalid: true);
        var host = $"https://registration-session-{Guid.NewGuid():N}.localhost";
        var token = await BootstrapAsync(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/identity/organizations/register")
        {
            Content = JsonContent.Create(new { email = "owner@example.test", password = "Testing1234!", legalName = "Northwind", cuit = "30-12345678-9" })
        };
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().ShouldBe("invalid_session");
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
    }

    private static async Task<string> BootstrapAsync(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
    }
}
