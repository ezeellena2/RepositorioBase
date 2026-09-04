using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Domain.IdentityAccess.Auditing;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// IA-REQ-029 at the places a shape test cannot reach. The response-graph guard proves no invitation request
/// *declares* a member that could carry the token; it cannot prove that no handler puts the token into a value
/// that already exists — <c>ApplicationError.Detail</c> is a plain string named neither "token" nor "secret", and
/// a log line, an audit metadata entry or a Problem Details body are not typed at all.
/// <para>
/// These tests therefore drive the real requests and look for the minted token in every channel that leaves the
/// process. They are deliberately written against the token the generator minted, not against a placeholder, so a
/// handler that echoes it anywhere is caught wherever it echoes it.
/// </para>
/// </summary>
public sealed class InvitationSecretHygieneTests : TestBase
{
    [Test]
    public async Task No_invitation_failure_carries_the_token_in_its_detail_or_the_logs()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        var token = TestApp.RawTokenAt(0);
        TestApp.ResetCapturedLogs();

        var bystander = await InvitationScenario.SeedConfirmedRecipientAsync($"bystander-{Guid.NewGuid():N}@example.test");
        TestApp.SetUserId(bystander);
        TestApp.SetCurrentTenant(null);
        var refused = await TestApp.SendAsync(new AcceptInvitationCommand(token));

        refused.IsFailure.ShouldBeTrue();
        // A failure detail is shown to the caller and may be logged, so it is a leak channel like any other.
        refused.Error!.Detail?.ShouldNotContain(token);
        refused.Error.Code.ShouldNotContain(token);
        TestApp.CapturedLogs.ShouldAllBe(entry => !entry.Contains(token), "an invitation token must never reach a log entry");
    }

    /// <summary>
    /// Issuing is the only moment the usable token exists in this process, so it is the moment to look for it in
    /// every channel at once: what the caller is answered with over HTTP, the <c>Location</c> it is handed, the
    /// application log, and the audit record. Anywhere it appears, it has left the envelope that was supposed to
    /// be its only home (IA-REQ-018/029).
    /// </summary>
    [Test]
    public async Task The_issued_token_reaches_no_response_header_log_or_audit_record()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        TestApp.SetUserId(organization.InviterIdentityId);
        TestApp.SetCurrentTenant(organization.TenantId);
        TestApp.SetApplicationPermissionGranted(true);
        TestApp.SetHttpAuthorizationGranted(true);
        TestApp.ResetCapturedLogs();

        using var antiforgeryRequest = new HttpRequestMessage(HttpMethod.Get, "https://issued.localhost/api/identity/antiforgery");
        var antiforgery = (await (await FunctionalTestSetup.HttpClient.SendAsync(antiforgeryRequest)).Content
            .ReadFromJsonAsync<Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        using var issue = new HttpRequestMessage(HttpMethod.Post, $"https://issued.localhost/api/tenants/{organization.TenantId.Value}/invitations")
        {
            Content = JsonContent.Create(new { email = $"invitee-{Guid.NewGuid():N}@example.test", roleIds = new[] { organization.RoleId } })
        };
        issue.Headers.Add("Origin", "https://issued.localhost");
        issue.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var response = await FunctionalTestSetup.HttpClient.SendAsync(issue);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = TestApp.RawTokenAt(0);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(token);
        response.Headers.Location!.ToString().ShouldNotContain(token);
        TestApp.CapturedLogs.ShouldAllBe(entry => !entry.Contains(token), "the minted token must not reach the application log");
        (await TestApp.ListAsync<AuditEvent>()).ShouldAllBe(audit =>
            !audit.CorrelationId.Contains(token) && audit.Metadata.All(entry => !entry.Value.Contains(token)));
        (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>()).Single().Ciphertext!.ShouldNotContain(token);
    }

    [Test]
    public async Task No_audit_record_written_by_an_invitation_carries_the_token()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        (await TestApp.SendAsync(new InviteMemberCommand(
            organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        var token = TestApp.RawTokenAt(0);
        var audits = await TestApp.ListAsync<AuditEvent>();
        audits.ShouldNotBeEmpty("issuing an invitation is an audited membership change");
        foreach (var audit in audits)
        {
            audit.CorrelationId.ShouldNotContain(token);
            foreach (var entry in audit.Metadata)
            {
                entry.Key.ShouldNotContain(token);
                entry.Value.ShouldNotContain(token);
            }
        }
    }

    /// <summary>
    /// The channel a client actually reads. A token echoed into a Problem Details body, or into a header such as
    /// <c>Location</c>, leaves the process just as surely as one echoed into a payload.
    /// </summary>
    [Test]
    public async Task No_invitation_response_echoes_the_submitted_token_in_its_body_or_headers()
    {
        var token = TestApp.RawTokenAt(4);
        using var antiforgeryRequest = new HttpRequestMessage(HttpMethod.Get, "https://hygiene.localhost/api/identity/antiforgery");
        var antiforgeryResponse = await FunctionalTestSetup.HttpClient.SendAsync(antiforgeryRequest);
        var antiforgery = (await antiforgeryResponse.Content.ReadFromJsonAsync<Web.Endpoints.AntiforgeryResponse>())!.RequestToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://hygiene.localhost/api/invitations/register")
        {
            Content = JsonContent.Create(new { token, password = "Testing1234!" })
        };
        request.Headers.Add("Origin", "https://hygiene.localhost");
        request.Headers.Add("X-CSRF-TOKEN", antiforgery);
        var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(token);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            foreach (var value in header.Value)
            {
                value.ShouldNotContain(token);
            }
        }
    }

    /// <summary>
    /// The public registration request is marked sensitive, so the pipeline must not log its body. Without this
    /// the neutral flow leaks both the token and the submitted password into the application log.
    /// </summary>
    [Test]
    public async Task A_sensitive_invitation_request_never_logs_its_own_payload()
    {
        TestApp.ResetCapturedLogs();

        await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(6), "Testing1234!"));

        TestApp.CapturedLogs.ShouldAllBe(entry => !entry.Contains(TestApp.RawTokenAt(6)));
        TestApp.CapturedLogs.ShouldAllBe(entry => !entry.Contains("Testing1234!"));
    }
}
