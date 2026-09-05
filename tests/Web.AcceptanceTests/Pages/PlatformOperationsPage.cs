using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

/// <summary>The public page that resends a bootstrap owner invitation without naming anybody.</summary>
public sealed class PlatformRecoveryPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/platform/bootstrap/recover";

    public async Task ResendAsync()
    {
        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Resend" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/bootstrap/recover", StringComparison.Ordinal));
    }

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("If an owner invitation is waiting");

    /// <summary>The page must not offer anywhere to name a recipient; that is the requirement, not the styling.</summary>
    public async Task AssertAcceptsNoRecipientAsync()
    {
        (await Page.Locator("input").CountAsync()).ShouldBe(0);
        (await Page.GetByText(new Regex("@", RegexOptions.IgnoreCase)).CountAsync()).ShouldBe(0);
    }
}

/// <summary>The Platform invitee's own pages: registration, and the invitation-bound MFA ceremony.</summary>
public sealed class PlatformInvitationPages(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/platform/invitations/register";

    public async Task GotoRegisterAsync(string token)
    {
        await Page.GotoAsync($"{BaseUrl}/platform/invitations/register#token={Uri.EscapeDataString(token)}");
        await Page.ReloadAsync();
    }

    public async Task RegisterAsync(string password)
    {
        await Page.FillAsync("#platform-password", password);
        await Page.RunAndWaitForResponseAsync(
            () => Page.Locator("button[type='submit']").ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/invitations/register", StringComparison.Ordinal));
    }

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Check your email.");

    public async Task GotoMfaAsync(string token)
    {
        await Page.GotoAsync($"{BaseUrl}/platform/mfa#token={Uri.EscapeDataString(token)}");
        await Page.ReloadAsync();
    }

    /// <summary>
    /// The whole ceremony, in the order the page enforces. The code is computed from the key the page just showed,
    /// which is exactly what an authenticator would do with it.
    /// </summary>
    public async Task CompleteMfaAsync()
    {
        var enrolled = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Begin enrollment" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/mfa/enroll", StringComparison.Ordinal));
        if (enrolled.Status != 200)
        {
            // A refused gate renders a problem instead of a key; reading it out is the difference between
            // "an element is missing" and knowing which of the four conditions was not met.
            throw new InvalidOperationException($"Enrollment answered {enrolled.Status}: {await enrolled.TextAsync()}");
        }

        var sharedKey = await Page.GetByTestId("platform-shared-key").InnerTextAsync();
        await Page.FillAsync("#platform-mfa-code", PlatformFixtures.TotpCode(sharedKey));
        await Page.RunAndWaitForResponseAsync(
            () => Page.Locator("button[type='submit']").ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/mfa/verify", StringComparison.Ordinal));

        // The membership only exists as of the acknowledgement, so the page then selects it as the session's
        // active tenant. Waiting for that selection rather than for the acknowledgement is what makes the
        // ceremony finished: navigating in between would find a session that belongs to nothing yet.
        await Page.RunAndWaitForResponseAsync(
            async () =>
            {
                await Page.GetByRole(AriaRole.Button, new() { Name = "I have saved my recovery codes" }).ClickAsync();
                await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("second factor is active");
            },
            candidate => candidate.Url.EndsWith("/api/identity/context/tenant", StringComparison.Ordinal) &&
                         candidate.Request.Method == "PUT" && candidate.Status == 200);
    }

    public async Task<string> ReadSharedKeyAsync() => await Page.GetByTestId("platform-shared-key").InnerTextAsync();
}

/// <summary>The panel itself.</summary>
public sealed class PlatformOperationsPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/platform";

    public Task AssertOfferedAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Organizations" })).ToBeVisibleAsync();

    public Task AssertNotOfferedAsync() =>
        Assertions.Expect(Page.GetByText(new Regex("MFA-authenticated Platform administrator"))).ToBeVisibleAsync();

    public async Task StepUpAsync(string sharedKey)
    {
        await Page.FillAsync("#platform-step-up", PlatformFixtures.TotpCode(sharedKey));
        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Form, new() { Name = "Step up" }).GetByRole(AriaRole.Button).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/mfa/step-up", StringComparison.Ordinal));
    }

    public async Task SuspendAsync(string slug, string reason)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Suspend {slug}" }).ClickAsync();
        await Page.SelectOptionAsync("#platform-suspension-reason", reason);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Confirm suspension" }).ClickAsync(),
            candidate => candidate.Url.Contains("/suspend", StringComparison.Ordinal));
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Suspension answered {response.Status}: {await response.TextAsync()}");
        }
    }

    public async Task ReactivateAsync(string slug)
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = $"Reactivate {slug}" }).ClickAsync(),
            candidate => candidate.Url.Contains("/reactivate", StringComparison.Ordinal));
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Reactivation answered {response.Status}: {await response.TextAsync()}");
        }
    }

    public Task AssertAuditContainsAsync(string eventType) =>
        Assertions.Expect(Page.GetByRole(AriaRole.List, new() { Name = "Audit" })).ToContainTextAsync(eventType);

    /// <summary>
    /// The capabilities Platform must never grow, checked on the surface a person actually touches. A control that
    /// does not exist cannot be reached by accident, however the API behaves.
    /// </summary>
    public async Task AssertNoProhibitedCapabilityAsync()
    {
        foreach (var forbidden in new[] { "impersonate", "delete", "act as", "become" })
        {
            (await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(forbidden, RegexOptions.IgnoreCase) }).CountAsync())
                .ShouldBe(0, $"the panel must offer no {forbidden} control.");
        }

        (await Page.Locator("select#platform-tenant, input[name='tenantId']").CountAsync())
            .ShouldBe(0, "the acting tenant comes from the session, never from the panel.");
    }

    public async Task AssertCannotInviteAdministratorAsync() =>
        (await Page.GetByLabel("Invite an administrator").CountAsync()).ShouldBe(0);
}
