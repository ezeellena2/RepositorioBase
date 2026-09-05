using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

/// <summary>The sign-in page of the identity experience, addressed by the headings a person actually reads.</summary>
public sealed class IdentitySignInPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/login";

    public Task SignInAsync(string email, string password) => SubmitAsync(email, password);

    private async Task SubmitAsync(string email, string password)
    {
        await Page.FillAsync("#login-email", email);
        await Page.FillAsync("#login-password", password);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AssertVisibleAsync() => Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Sign in");

    public Task AssertProblemAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
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
}

/// <summary>The two halves of an invitation. Both carry their token in the URL fragment, never in the query.</summary>
public sealed class InvitationPages(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/invitations/accept";

    public Task GotoRegisterAsync(string token) => Page.GotoAsync($"{BaseUrl}/invitations/register#token={Uri.EscapeDataString(token)}");

    public Task GotoAcceptAsync(string token) => Page.GotoAsync($"{BaseUrl}/invitations/accept#token={Uri.EscapeDataString(token)}");

    public async Task RegisterAsync(string password)
    {
        await Page.FillAsync("#invitation-password", password);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AcceptAsync() => Page.GetByRole(AriaRole.Button, new() { Name = "Accept" }).ClickAsync();

    public Task AssertAcceptedAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("You are a member now.");

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Check your email.");

    /// <summary>The token must not survive in the address bar, in history, or in anything that logs a URL.</summary>
    public Task AssertFragmentClearedAsync() => Assertions.Expect(Page).Not.ToHaveURLAsync(new Regex("#token="));
}

public sealed class InviteMemberPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/members/invite";

    public async Task InviteAsync(string email, string roleId)
    {
        await Page.FillAsync("#invite-email", email);
        await Page.FillAsync("#invite-roles", roleId);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AssertSentAsync() => Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Invitation sent.");
}
