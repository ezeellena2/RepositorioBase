using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

/// <summary>The sign-in page of the identity experience, addressed by the headings a person actually reads.</summary>
public sealed class IdentitySignInPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/login";

    /// <summary>
    /// Signs in and waits for it to have happened: the page stops being the sign-in page, which is what the
    /// form does once the session exists. Clicking and moving on would abandon the request mid-flight — the
    /// next navigation cancels it and no session is ever created.
    /// </summary>
    public async Task SignInAsync(string email, string password)
    {
        var response = await AttemptSignInAsync(email, password);
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Sign-in answered {response.Status}: {await response.TextAsync()}");
        }

        await Assertions.Expect(Page.Locator("h1")).Not.ToHaveTextAsync("Sign in");
    }

    /// <summary>Submits and waits for the answer without requiring one, for the journeys that must be refused.</summary>
    public Task<IResponse> AttemptSignInAsync(string email, string password) =>
        Page.RunAndWaitForResponseAsync(
            async () =>
            {
                await Page.FillAsync("#login-email", email);
                await Page.FillAsync("#login-password", password);
                await Page.Locator("button[type='submit']").ClickAsync();
            },
            candidate => candidate.Url.EndsWith("/api/identity/sessions", StringComparison.Ordinal) && candidate.Request.Method == "POST");

    /// <summary>Leaves the session through the navigation, so the next journey starts from a signed-out browser.</summary>
    public async Task SignOutAsync()
    {
        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Link, new() { Name = "Log out" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/sessions/current", StringComparison.Ordinal) && candidate.Request.Method == "DELETE");
        await AssertVisibleAsync();
    }

    public Task AssertVisibleAsync() => Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Sign in");

    public Task AssertProblemAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
}

/// <summary>
/// The screen both confirmation mails open. It is a page object rather than a fixture because that is the point:
/// confirming used to be an UPDATE the harness ran, so nothing proved the link a recipient receives leads
/// anywhere (IA-REQ-005).
/// </summary>
public sealed class ConfirmEmailPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/confirm-email";

    /// <summary>
    /// Opens the link exactly as delivered. There is no reload here, unlike the invitation pages: those
    /// re-navigate to the path they are already on, which is a same-document change the SPA would ignore. This
    /// link is always arrived at from somewhere else, and reloading after the page has erased its own fragment
    /// would throw the token away.
    /// </summary>
    internal Task OpenDeliveredAsync(PlatformFixtures.DeliveredMessage delivered) =>
        Page.GotoAsync($"{BaseUrl}{delivered.Path}{delivered.Fragment}");

    /// <summary>The token must not survive in the address bar, in history, or in anything that logs a URL.</summary>
    public Task AssertFragmentClearedAsync() => Assertions.Expect(Page).Not.ToHaveURLAsync(new Regex("#token="));

    public async Task ConfirmAsync()
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my address" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/confirm-email", StringComparison.Ordinal) &&
                         candidate.Request.Method == "POST");
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Confirmation answered {response.Status}: {await response.TextAsync()}");
        }

        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Your address is confirmed");
    }
}

/// <summary>Registering an organization, whose answer is deliberately the same whatever happened.</summary>
public sealed class RegisterOrganizationPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/organizations/register";

    public async Task RegisterAsync(string legalName, string cuit, string email, string password)
    {
        await Page.FillAsync("#register-legal-name", legalName);
        await Page.FillAsync("#register-cuit", cuit);
        await Page.FillAsync("#register-email", email);
        await Page.FillAsync("#register-password", password);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("If that address can register");
}

/// <summary>What the session actually grants, as the server reports it.</summary>
public sealed class IdentityContextPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/identity";

    public Task AssertActiveOrganizationAsync(string name) =>
        Assertions.Expect(Page.Locator("dd").Nth(1)).ToHaveTextAsync(name);

    public Task AssertVisibleAsync() => Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Your access");
}

public sealed class TenantSelectorPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/organizations/select";

    public Task ChooseAsync(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name }).ClickAsync();

    public Task AssertOffersAsync(string name) =>
        Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = name })).ToBeVisibleAsync();

    /// <summary>
    /// The other context this identity is offered, named by the screen rather than by the test. A personal
    /// tenant is known by the slug it was given, which nobody outside the application chose — so a journey that
    /// asserted a name of its own would be asserting its own guess.
    /// </summary>
    public async Task<string> OtherThanAsync(string known)
    {
        // Scoped to the screen's own region: the page furniture is lists too, and counting those would be
        // counting the navigation.
        var offered = Page.GetByRole(AriaRole.Region, new() { Name = "Choose an organization" }).GetByRole(AriaRole.Button);
        await Assertions.Expect(offered).ToHaveCountAsync(2);
        var names = await offered.AllInnerTextsAsync();
        return names
            .Select(name => name.Replace(" (current)", string.Empty, StringComparison.Ordinal).Trim())
            .Single(name => !string.Equals(name, known, StringComparison.Ordinal));
    }
}

/// <summary>The two halves of an invitation. Both carry their token in the URL fragment, never in the query.</summary>
public sealed class InvitationPages(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/invitations/accept";

    public Task GotoRegisterAsync(string token) => Page.GotoAsync($"{BaseUrl}/invitations/register#token={Uri.EscapeDataString(token)}");

    /// <summary>
    /// Opens the invitation link the way its recipient does: as a fresh document. Going to the same path with
    /// only a different fragment is a same-document navigation — the page stays mounted, keeps whatever it was
    /// showing, and never reads the new token. Following a link out of an email is not that, and a step that
    /// accepted a second time would otherwise be asserting against the first attempt's screen.
    /// </summary>
    public async Task GotoAcceptAsync(string token)
    {
        await Page.GotoAsync($"{BaseUrl}/invitations/accept#token={Uri.EscapeDataString(token)}");
        await Page.ReloadAsync();
    }

    public async Task RegisterAsync(string password)
    {
        await Page.FillAsync("#invitation-password", password);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AcceptAsync() => Page.GetByRole(AriaRole.Button, new() { Name = "Accept" }).ClickAsync();

    /// <summary>
    /// A refused acceptance renders a problem instead of an outcome, so the refusal is read out here. Without
    /// it the failure is only that an element is missing, which says nothing about why.
    /// </summary>
    public async Task AssertAcceptedAsync()
    {
        var problem = Page.GetByRole(AriaRole.Alert);
        if (await problem.CountAsync() > 0 && await problem.IsVisibleAsync())
        {
            throw new InvalidOperationException($"Accepting was refused: {await problem.InnerTextAsync()}");
        }

        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("You are a member now.");
    }

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Check your email.");

    /// <summary>The token must not survive in the address bar, in history, or in anything that logs a URL.</summary>
    public Task AssertFragmentClearedAsync() => Assertions.Expect(Page).Not.ToHaveURLAsync(new Regex("#token="));
}

public sealed class InviteMemberPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/members/invite";

    /// <summary>
    /// The role is chosen by the name a person reads, not by an identifier. The screen offers this
    /// organization's own roles as checkboxes, so a name that is not offered fails here rather than being sent
    /// to the server as an unknown identifier.
    /// </summary>
    public async Task InviteAsync(string email, string roleName)
    {
        await Page.FillAsync("#invite-email", email);
        await Page.GetByLabel(roleName, new() { Exact = true }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
    }

    public Task AssertSentAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invitation sent.");

    public Task AssertOfferStandsAsync(string email) =>
        Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Resend to {email}" })).ToBeVisibleAsync();
}
