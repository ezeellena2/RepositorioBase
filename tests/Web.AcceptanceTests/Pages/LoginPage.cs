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

        // The cookie goes into the browser's own jar, which is where a real session lives. It used to be
        // injected as an extra HTTP header instead, on the belief that Chromium rejects Secure __Host- cookies
        // over HTTP — but http://localhost is a secure context, so it accepts them. That header was also silently
        // dropped the moment the jar held any cookie for this origin: the browser rebuilds Cookie from the jar
        // and the extra header does not survive. One antiforgery bootstrap was enough to take the session with
        // it, and every authenticated page load then answered 401 to its own context read.
        var separatorIndex = cookie.IndexOf('=');
        return Page.Context.AddCookiesAsync([
            new Cookie
            {
                Name = cookie[..separatorIndex],
                Value = cookie[(separatorIndex + 1)..],
                // A __Host- cookie may only be stored for a secure URL, and Aspire serves the test frontend
                // over HTTP. Cookies ignore the port, so it is stored against https://<host> and the browser
                // still sends it to the HTTP frontend: Chromium treats localhost as a secure context.
                Url = $"https://{new Uri(BaseUrl).Host}",
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteAttribute.Lax
            }
        ]);
    }

    public Task ClearAuthenticationAsync()
    {
        authenticationCookie = null;
        return Page.Context.ClearCookiesAsync();
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
