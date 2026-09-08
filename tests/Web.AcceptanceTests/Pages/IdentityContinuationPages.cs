using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

/// <summary>A stranger setting up an account of their own, whose answer is the same whatever happened.</summary>
public sealed class PersonalRegisterPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/personal/register";

    public async Task RegisterAsync(string fullName, string displayName, string document, string email, string password)
    {
        await Page.FillAsync("#personal-full-name", fullName);
        await Page.FillAsync("#personal-display-name", displayName);
        await Page.FillAsync("#personal-document", document);
        await Page.FillAsync("#personal-email", email);
        await Page.FillAsync("#personal-password", password);
        await Page.Locator("button[type='submit']").ClickAsync();
    }

    public Task AssertNeutralAcknowledgementAsync() =>
        Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("If that address can register");
}

/// <summary>
/// A person's own profile, and the one place an identity that already exists claims a personal context.
/// <para>
/// The masked number is the only form of a document this screen has ever held — the full one is sealed and the
/// route that would read it back does not exist — so a journey that finds the submitted digits anywhere on this
/// page has found a leak rather than a rendering choice.
/// </para>
/// </summary>
public sealed class PersonalProfilePage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/identity/profile";

    public async Task AddPersonalContextAsync(string fullName, string displayName, string document)
    {
        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("You have no personal context yet.");
        await Page.FillAsync("#add-personal-full-name", fullName);
        await Page.FillAsync("#add-personal-display-name", displayName);
        await Page.FillAsync("#add-personal-document", document);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Add my personal account" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/personal", StringComparison.Ordinal) &&
                         candidate.Request.Method == "POST");
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Claiming a personal context answered {response.Status}: {await response.TextAsync()}");
        }
    }

    public async Task AssertDocumentMaskedAsync(string submittedNumber)
    {
        await Assertions.Expect(Page.Locator("dl")).ToContainTextAsync("DNI");
        var shown = await Page.Locator("dl").InnerTextAsync();
        shown.Contains('•').ShouldBeTrue("a recorded document is shown masked or not at all");
        shown.Contains(submittedNumber, StringComparison.Ordinal)
            .ShouldBeFalse("the number a person submitted is sealed; nothing reads it back");
        (await Page.ContentAsync()).Contains(submittedNumber, StringComparison.Ordinal)
            .ShouldBeFalse("not in the markup either, masked or otherwise");
    }
}

/// <summary>The devices a person holds, and the two ways they end one.</summary>
public sealed class OwnDevicesPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/identity/sessions";

    public Task AssertVisibleAsync() => Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Your devices");

    /// <summary>
    /// Ending a device asks for the password first, which is the point of the field: a stolen session cannot end
    /// the sessions of the person it was stolen from.
    /// </summary>
    public Task ProveAsync(string password) => Page.FillAsync("#sessions-password", password);

    public Task EndTheOtherDeviceAsync() => RevokeAsync("End this device");

    public Task EndEveryOtherDeviceAsync() => RevokeAsync("End every other device");

    private async Task RevokeAsync(string control)
    {
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = control }).First.ClickAsync(),
            candidate => candidate.Url.Contains("/api/identity/sessions", StringComparison.Ordinal) &&
                         candidate.Request.Method == "DELETE");
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"\"{control}\" answered {response.Status}: {await response.TextAsync()}");
        }
    }
}

/// <summary>The two halves of a password moving: a mailed link for somebody locked out, and a change from inside.</summary>
public sealed class PasswordPages(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/credentials/forgot";

    public async Task AskForALinkAsync(string email)
    {
        await Page.FillAsync("#forgot-email", email);
        await Page.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("If that address can sign in");
    }

    /// <summary>
    /// Opens the link exactly as delivered. Like the confirmation link it is always arrived at from elsewhere,
    /// and the page erases its own fragment, so reloading afterwards would throw the token away.
    /// </summary>
    internal Task OpenDeliveredAsync(PlatformFixtures.DeliveredMessage delivered) =>
        Page.GotoAsync($"{BaseUrl}{delivered.Path}{delivered.Fragment}");

    public async Task ChooseAsync(string password)
    {
        await Page.FillAsync("#reset-password", password);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Set my password" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/credentials/password/reset", StringComparison.Ordinal) &&
                         candidate.Request.Method == "POST");
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Choosing a password answered {response.Status}: {await response.TextAsync()}");
        }

        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Your password is set");
    }

    /// <summary>The token must not survive in the address bar, in history, or in anything that logs a URL.</summary>
    public Task AssertFragmentClearedAsync() => Assertions.Expect(Page).Not.ToHaveURLAsync(new Regex("#token="));

    public async Task ChangeAsync(string current, string next)
    {
        await Page.GotoAsync($"{BaseUrl}/identity/password");
        await Page.FillAsync("#change-current", current);
        await Page.FillAsync("#change-next", next);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Change it" }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/credentials/password", StringComparison.Ordinal) &&
                         candidate.Request.Method == "PUT");
        if (response.Status != 204)
        {
            throw new InvalidOperationException($"Changing the password answered {response.Status}: {await response.TextAsync()}");
        }

        await Assertions.Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Your password is changed");
    }
}
