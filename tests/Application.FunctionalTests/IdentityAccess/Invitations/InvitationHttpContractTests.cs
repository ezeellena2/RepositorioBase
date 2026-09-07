using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Web.Endpoints;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// The external contract of the invitation routes SPEC section 6 declares, at the boundary a client actually
/// sees: a semantic status, an endpoint DTO, and — for the created invitation — a <c>Location</c>. What
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

    /// <summary>
    /// An empty identifier is decidable from the request alone, so it is a 400 with the declared code. Reaching
    /// the strongly-typed identifier factory with it throws, and the caller would read <c>internal_server_error</c>
    /// for input the boundary could have refused.
    /// </summary>
    [TestCase("00000000-0000-0000-0000-000000000000", "role")]
    [TestCase("tenant", "00000000-0000-0000-0000-000000000000")]
    public async Task An_empty_identifier_is_a_declared_400_and_never_an_internal_error(string emptyTenant, string emptyRole)
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        await AuthenticateAsync(organization.InviterIdentityId, organization);
        var antiforgery = await AntiforgeryAsync();
        var tenantId = emptyTenant == "tenant" ? organization.TenantId.Value : Guid.Empty;
        var roleId = emptyRole == "role" ? organization.RoleId : Guid.Empty;

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/invitations",
            new { email = $"invitee-{Guid.NewGuid():N}@example.test", roleIds = new[] { roleId } },
            antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("invalid_invitation");
        (await TestApp.CountAsync<Domain.IdentityAccess.Invitations.Invitation>()).ShouldBe(0);
    }

    /// <summary>
    /// A lost optimistic update is the declared 409, not a 500. The competing write is armed against the
    /// invitation's own row version, which is what the handler has to translate.
    /// </summary>
    [Test]
    public async Task A_lost_optimistic_update_answers_the_declared_409()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new Application.IdentityAccess.Invitations.InviteMember.InviteMemberCommand(
            organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        TestApp.EnableInvitationConcurrencyConflict();
        var result = await TestApp.SendAsync(new Application.IdentityAccess.Invitations.ResendInvitation.ResendInvitationCommand(
            organization.TenantId, (await InvitationScenario.SingleInvitationAsync()).Id.Value));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invitation_conflict");
        Web.Infrastructure.ApiProblemDetailsMapper.GetStatusCode(result.Error.Category).ShouldBe(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Reissuing is reachable over HTTP and answers nothing at all. The rotated token belongs in the recipient's
    /// envelope; a response body here would be the one place a caller could read it (IA-REQ-015/017/018).
    /// </summary>
    [Test]
    public async Task Reissuing_answers_a_bodyless_204_and_rotates_the_token_without_showing_it()
    {
        var organization = await IssuedInvitationAsync();
        var before = (await InvitationScenario.SingleInvitationAsync()).TokenHash;
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/invitations/{(await InvitationScenario.SingleInvitationAsync()).Id.Value}/resend",
            new { },
            antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty("a reissue has nothing a caller may hold");
        var after = await InvitationScenario.SingleInvitationAsync();
        after.Status.ShouldBe(Domain.IdentityAccess.Invitations.InvitationStatus.Pending);
        after.TokenHash.ShouldNotBe(before, "the previous token stops being usable in the same transaction");
    }

    [Test]
    public async Task Withdrawing_answers_a_bodyless_204_and_ends_the_offer()
    {
        var organization = await IssuedInvitationAsync();
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/invitations/{(await InvitationScenario.SingleInvitationAsync()).Id.Value}/cancel",
            new { },
            antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await InvitationScenario.SingleInvitationAsync()).Status
            .ShouldBe(Domain.IdentityAccess.Invitations.InvitationStatus.Cancelled);
    }

    /// <summary>
    /// The same boundary rule the issuing route has. A route tenant the session is not operating in is refused
    /// rather than honoured, so an offer cannot be reissued or withdrawn from outside its own organization.
    /// </summary>
    [TestCase("resend")]
    [TestCase("cancel")]
    public async Task Acting_on_an_offer_from_a_tenant_the_session_is_not_operating_in_is_refused(string action)
    {
        await IssuedInvitationAsync();
        var invitationId = (await InvitationScenario.SingleInvitationAsync()).Id.Value;
        var other = await InvitationScenario.SeedOrganizationAsync();
        var antiforgery = await AntiforgeryAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{other.TenantId.Value}/invitations/{invitationId}/{action}",
            new { },
            antiforgery);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await InvitationScenario.SingleInvitationAsync()).Status
            .ShouldBe(Domain.IdentityAccess.Invitations.InvitationStatus.Pending);
    }

    private static async Task<InvitationScenario.Organization> IssuedInvitationAsync()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersManage);
        InvitationScenario.ActAs(organization);
        (await TestApp.SendAsync(new Application.IdentityAccess.Invitations.InviteMember.InviteMemberCommand(
            organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        await AuthenticateAsync(organization.InviterIdentityId, organization);
        return organization;
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
