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

    /// <summary>
    /// Opens a link exactly as it was delivered. Only the origin is this run's own: the mail is rendered from a
    /// configured public origin, and the host a test allocates has a port nobody could have configured in
    /// advance. The path and the fragment — which is the whole of the secret — are the delivered ones.
    /// <para>
    /// The step away first is what makes it a real arrival. Navigating to the path the browser is already on with
    /// only a different fragment is a same-document change: the page stays mounted and never reads the new token.
    /// Reloading instead was a race — the page erases its own fragment on arrival, so a reload that lost it threw
    /// the token away — and leaving somewhere else first has neither problem.
    /// </para>
    /// </summary>
    internal async Task OpenDeliveredAsync(PlatformFixtures.DeliveredMessage delivered)
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.GotoAsync($"{BaseUrl}{delivered.Path}{delivered.Fragment}");
    }

    /// <summary>
    /// Presses the same button and expects to be refused. It is how a superseded link behaves: reissuing a
    /// confirmation retires the envelope the older one opens, so the older link stops working the moment the
    /// newer one is sent — which is what "one usable confirmation" has to mean.
    /// </summary>
    public async Task AssertConfirmationRefusedAsync()
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my address" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/invitations/confirm", StringComparison.Ordinal));
        response.Status.ShouldNotBe(204, "a retired confirmation link must not confirm anything.");
        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
    }

    /// <summary>One click, which is all the confirmation screen asks of someone who followed their own link.</summary>
    public async Task ConfirmAsync()
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my address" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/invitations/confirm", StringComparison.Ordinal));
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Confirmation answered {response.Status}: {await response.TextAsync()}");
        }

        await Assertions.Expect(Page.GetByRole(AriaRole.Status))
            .ToContainTextAsync("open your invitation email again to set up your second factor");
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

    /// <summary>
    /// Takes the continuation the invitation page offers a signed-in recipient. There is no URL to compose: the
    /// page is the one their mail already opened, and the token is the one it read out of that mail and still
    /// holds. Composing "/platform/mfa#token=..." here was the harness supplying a step the product did not have.
    /// </summary>
    public async Task ContinueToSecondFactorAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Set up your second factor" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Begin enrollment" })).ToBeVisibleAsync();
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

    /// <summary>
    /// The screen a session that holds the membership but has not proved the factor meets: the step-up, and no
    /// directory at all. Asserting both halves is the point — offering the form while still rendering the
    /// organizations would be the same leak with a prompt on top of it.
    /// </summary>
    public async Task AssertStepUpRequestedAsync()
    {
        await Assertions.Expect(Page.GetByRole(AriaRole.Form, new() { Name = "Step up" })).ToBeVisibleAsync();
        (await Page.GetByRole(AriaRole.Heading, new() { Name = "Organizations" }).CountAsync())
            .ShouldBe(0, "a session that has not proved the factor reads no directory.");
        (await Page.GetByRole(AriaRole.List, new() { Name = "Audit" }).CountAsync()).ShouldBe(0);
    }

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
        await Page.ClickAsync("#platform-suspension-reason");
        await Page.ClickAsync($"[role='option'][data-value='{reason}']");
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
        // Counting is not a waiting assertion, and a panel still fetching its identity context offers nothing at
        // all — which would pass this for the one reason that proves nothing. So the panel is waited for first.
        await AssertOfferedAsync();

        foreach (var forbidden in new[] { "impersonate", "delete", "act as", "become" })
        {
            (await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(forbidden, RegexOptions.IgnoreCase) }).CountAsync())
                .ShouldBe(0, $"the panel must offer no {forbidden} control.");
        }

        (await Page.Locator("#platform-tenant, input[name='tenantId']").CountAsync())
            .ShouldBe(0, "the acting tenant comes from the session, never from the panel.");
    }

    /// <summary>
    /// The form is offered only to a session that holds <c>platform.admins.manage</c>, so waiting for it is
    /// waiting for the panel to have decided — and the panel decides once, from the context it fetched.
    /// </summary>
    public Task AssertCanInviteAdministratorAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Form, new() { Name = "Invite an administrator" })).ToBeVisibleAsync();

    /// <summary>
    /// Invites, from the panel's own form. Inviting is a Platform change, so the factor is proved first — and
    /// that is the product's rule rather than the test's convenience: a session with a stale step-up is refused.
    /// </summary>
    public async Task InviteAdministratorAsync(string email, string sharedKey)
    {
        await AssertCanInviteAdministratorAsync();
        await StepUpAsync(sharedKey);
        await Page.FillAsync("#platform-invite-email", email);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Form, new() { Name = "Invite an administrator" })
                .GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/platform/admins/invitations", StringComparison.Ordinal) &&
                         candidate.Request.Method == "POST");
        if (response.Status is not (200 or 201 or 202 or 204))
        {
            throw new InvalidOperationException($"Inviting an administrator answered {response.Status}: {await response.TextAsync()}");
        }
    }
}

/// <summary>
/// The operator directory of accounts, and the two lifecycle changes it offers (IA-REQ-054).
/// <para>
/// Everything here is asked of the row a person is looking at. The status a journey reports is the cell the
/// screen rendered, not the column the database keeps: an operator acts on what they were shown, and a directory
/// that showed something else would be the defect worth catching.
/// </para>
/// </summary>
public sealed class PlatformIdentitiesPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/platform/identities";

    public Task AssertOfferedAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Identities" })).ToBeVisibleAsync();

    /// <summary>The status the screen states for one account, read off its row.</summary>
    public async Task<string> StatusOfAsync(string address)
    {
        var status = StatusCellOf(address);
        await Assertions.Expect(status).ToBeVisibleAsync();
        return (await status.InnerTextAsync()).Trim();
    }

    public Task AssertStatusAsync(string address, string status) =>
        Assertions.Expect(StatusCellOf(address)).ToHaveTextAsync(status);

    /// <summary>
    /// Opens the confirmation and chooses the reason on it. Neither change is done on a click, and the reason is
    /// picked from the closed set the screen offers — there is nowhere on it to type one of your own.
    /// </summary>
    public async Task ArmSuspensionAsync(string address, string reason)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Suspend {address}" }).ClickAsync();
        await Assertions.Expect(SuspensionForm).ToBeVisibleAsync();
        await Page.ClickAsync("#platform-identity-suspension-reason");
        await Page.ClickAsync($"[role='option'][data-value='{reason}']");
    }

    public async Task ConfirmSuspensionAsync()
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => SuspensionForm.GetByRole(AriaRole.Button, new() { Name = "Confirm suspension" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/suspend", StringComparison.Ordinal) &&
                         candidate.Url.Contains("/api/platform/identities/", StringComparison.Ordinal));
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Suspending an account answered {response.Status}: {await response.TextAsync()}");
        }
    }

    public async Task ArmReactivationAsync(string address)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Reactivate {address}" }).ClickAsync();
        await Assertions.Expect(ReactivationForm).ToBeVisibleAsync();
    }

    /// <summary>
    /// The acknowledgement arrives unticked. It is the operator saying they know where the account will land, so
    /// a form that offered it already ticked would be saying that on their behalf.
    /// </summary>
    public Task AssertAcknowledgementUntickedAsync() =>
        Assertions.Expect(Page.Locator("#platform-identity-acknowledge")).Not.ToBeCheckedAsync();

    public async Task ConfirmReactivationAsync()
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => ReactivationForm.GetByRole(AriaRole.Button, new() { Name = "Confirm reactivation" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/reactivate", StringComparison.Ordinal) &&
                         candidate.Url.Contains("/api/platform/identities/", StringComparison.Ordinal));
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Lifting a suspension answered {response.Status}: {await response.TextAsync()}");
        }
    }

    public Task CaptureAsync(string name) => PlatformScreenshots.CaptureAsync(Page, name);

    private ILocator SuspensionForm => Page.GetByRole(AriaRole.Form, new() { Name = "Confirm suspension" });

    private ILocator ReactivationForm => Page.GetByRole(AriaRole.Form, new() { Name = "Confirm reactivation" });

    /// <summary>The second cell of the account's row: Address, Account status, Identity, Actions.</summary>
    private ILocator StatusCellOf(string address) =>
        Page.GetByRole(AriaRole.Row).Filter(new() { HasText = address }).GetByRole(AriaRole.Cell).Nth(1);
}

/// <summary>
/// Retention: what this deployment's rules are. There is no purge control on it and there never will be, so a
/// journey here reads the policy rather than exercising one.
/// </summary>
public sealed class PlatformRetentionPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/platform/retention";

    public async Task AssertPolicyOfferedAsync()
    {
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Retention" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("retention-personal-data-mode")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("retention-active-holds")).ToBeVisibleAsync();
    }

    public Task CaptureAsync(string name) => PlatformScreenshots.CaptureAsync(Page, name);
}

/// <summary>
/// Where the operator screens are photographed. The pictures are a deliverable rather than a debugging aid — the
/// screens were commissioned to be looked at, and "passed" shows nobody what was built — and they are written
/// into the build's own ignored <c>artifacts/</c> tree, so a run leaves the repository as it found it.
/// </summary>
internal static class PlatformScreenshots
{
    private static string Folder { get; } = Path.Combine(ArtifactsRoot(), "screenshots", "platform-operator-screens");

    internal static Task CaptureAsync(IPage page, string name)
    {
        Directory.CreateDirectory(Folder);
        return page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(Folder, $"{name}.png"),
            FullPage = true
        });
    }

    /// <summary>
    /// The build puts this assembly under <c>artifacts/bin/…</c>, so the ignored tree is the one the run is
    /// already executing out of. Walking up to it by name survives a layout change that a counted number of
    /// parent directories would silently follow to the wrong place.
    /// </summary>
    private static string ArtifactsRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "artifacts", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
        }

        throw new InvalidOperationException($"No artifacts directory contains {AppContext.BaseDirectory}.");
    }
}
