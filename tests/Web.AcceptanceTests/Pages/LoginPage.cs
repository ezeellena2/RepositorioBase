namespace CleanArchitecture.Web.AcceptanceTests.Pages;

public class LoginPage(IPage page) : BasePage(page)
{
    private string? authenticationCookie;

    public override string PagePath => $"{BaseUrl}/login";

    public Task SetEmail(string email)
        => Page.FillAsync("#email", email);

    public Task SetPassword(string password)
        => Page.FillAsync("#password", password);

    public Task ClickLogin()
        => Page.Locator("button[type='submit']").ClickAsync();

    public Task SetAuthenticationCookieAsync(string cookie)
    {
        var separator = cookie.IndexOf('=');
        if (separator <= 0)
        {
            throw new ArgumentException("Authentication cookie is malformed.", nameof(cookie));
        }

        authenticationCookie = cookie;

        // Aspire exposes the browser test frontend through HTTP, where Chromium correctly
        // rejects Secure __Host- cookies from its jar. Keep the real issued value and send it
        // through this browser context's request state; functional tests assert its real flags.
        return Page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Cookie"] = cookie
        });
    }

    public Task ClearAuthenticationAsync()
    {
        authenticationCookie = null;
        return Page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>());
    }

    public Task<bool> HasAuthenticationCookieAsync() => Task.FromResult(
        !string.IsNullOrWhiteSpace(authenticationCookie) && authenticationCookie.StartsWith("__Host-ia-auth=", StringComparison.Ordinal));

    public string GetAuthenticationCookie() => authenticationCookie
        ?? throw new InvalidOperationException("The browser context does not have an authentication cookie.");

    public Task<string?> LogoutButtonText()
        => Page.Locator("a:has-text('Log out')").TextContentAsync();

    public Task AssertErrorVisible()
        => Assertions.Expect(Page.Locator("#login-error")).ToBeVisibleAsync();
}
