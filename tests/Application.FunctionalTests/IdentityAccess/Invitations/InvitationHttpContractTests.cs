using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Web.Endpoints;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// The external contract of the three invitation routes SPEC section 6 declares, at the boundary a client
/// actually sees: a semantic status, an endpoint DTO, and — for the created invitation — a <c>Location</c>. What
/// this pins beyond the Application tests is that Web serializes its own DTO and never the internal Result or a
/// <c>{ success, data, error }</c> envelope (IA-REQ-038).
/// </summary>
public sealed class InvitationHttpContractTests : TestBase
{
    private const string Host = "https://invitations.localhost";

    [Test]
    public async Task Issuing_an_invitation_answers_201_with_its_location_and_its_own_dto()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        await AuthenticateAsync(organization.InviterIdentityId, organization);
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/invitations",
            new { email = $"invitee-{Guid.NewGuid():N}@example.test", roleIds = new[] { organization.RoleId } },
            antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull("a created invitation is addressable (IA-REQ-038)");
        var body = await ReadJsonAsync(response);
        body.GetProperty("invitationId").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        body.TryGetProperty("token", out _).ShouldBeFalse("the usable token never reaches the issuer (IA-REQ-015/018)");
        body.TryGetProperty("success", out _).ShouldBeFalse();
        body.TryGetProperty("data", out _).ShouldBeFalse();
        body.TryGetProperty("value", out _).ShouldBeFalse("the internal Result is never serialized");
        response.Headers.Location!.ToString().ShouldContain(body.GetProperty("invitationId").GetGuid().ToString());
    }

    /// <summary>
    /// The public registration route is bodyless and neutral by declaration, so an unknown token gets exactly what
    /// a live one gets. A 404 or a 400 here would turn the endpoint into a token oracle.
    /// </summary>
    [Test]
    public async Task Registering_from_an_invitation_answers_a_bodyless_202_even_for_an_unknown_token()
    {
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            "/api/invitations/register",
            new { token = TestApp.RawTokenAt(3), password = "Testing1234!" },
            antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty("the neutral 202 carries no body to read state out of");
    }

    [Test]
    public async Task Accepting_answers_200_with_the_acceptance_dto()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        await AuthenticateAsync(organization.InviterIdentityId, organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new Application.IdentityAccess.Invitations.InviteMember.InviteMemberCommand(
            organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        var recipientId = await InvitationScenario.SeedConfirmedRecipientAsync(email);
        await AuthenticateAsync(recipientId, organization: null);
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/invitations/accept", new { token = TestApp.RawTokenAt(0) }, antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("tenantId").GetGuid().ShouldBe(organization.TenantId.Value);
        body.GetProperty("membershipId").GetGuid().ShouldNotBe(Guid.Empty);
        body.TryGetProperty("success", out _).ShouldBeFalse();
        body.TryGetProperty("value", out _).ShouldBeFalse();
    }

    /// <summary>
    /// The failure half of the same contract: a rejected acceptance is RFC 9457 with the declared stable code, not
    /// a bare status and not a leaked exception.
    /// </summary>
    [Test]
    public async Task A_rejected_acceptance_is_problem_details_with_the_declared_code()
    {
        var identityId = await TestApp.RunAsDefaultUserAsync();
        await AuthenticateAsync(identityId, organization: null);
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/invitations/accept", new { token = TestApp.RawTokenAt(5) }, antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("invalid_invitation");
        string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()).ShouldBeFalse();
        body.TryGetProperty("errors", out _).ShouldBeFalse("an unusable token is not a field-level validation failure");
    }

    /// <summary>
    /// The route tenant is a client-supplied value like any other. Issuing into a tenant the session is not
    /// operating in must be refused at the boundary, not honoured because the path said so.
    /// </summary>
    [Test]
    public async Task Issuing_into_a_tenant_the_session_is_not_operating_in_is_refused()
    {
        var administered = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        var other = await InvitationScenario.SeedOrganizationAsync();
        await AuthenticateAsync(administered.InviterIdentityId, administered);
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{other.TenantId.Value}/invitations",
            new { email = $"invitee-{Guid.NewGuid():N}@example.test", roleIds = new[] { administered.RoleId } },
            antiforgery);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await TestApp.CountAsync<Domain.IdentityAccess.Invitations.Invitation>()).ShouldBe(0);
    }

    private static async Task AuthenticateAsync(Guid identityId, InvitationScenario.Organization? organization)
    {
        TestApp.SetUserId(identityId);
        TestApp.SetCurrentTenant(organization?.TenantId);
        TestApp.SetHttpAuthorizationGranted(true);
        TestApp.SetApplicationPermissionGranted(true);
        await Task.CompletedTask;
    }

    private static async Task<string> AntiforgeryAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Host}/api/identity/antiforgery");
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        return payload!.RequestToken;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, string antiforgery)
    {
        using var request = new HttpRequestMessage(method, $"{Host}{path}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", Host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        return await FunctionalTestSetup.HttpClient.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
