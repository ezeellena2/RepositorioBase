using System.Diagnostics;

namespace CleanArchitecture.Web.AcceptanceTests;

[SetUpFixture]
public class PlaywrightSetup
{
    private static bool IsHeadless => Debugger.IsAttached is false;
    private static IPlaywright? _playwright;

    public static IBrowser Browser { get; private set; } = null!;

    /// <summary>
    /// A context for the dev server, which presents the ASP.NET development certificate. Whether that
    /// certificate is trusted is a property of the machine, not of the behaviour under test, so a run on an
    /// untrusted one must still exercise the journey rather than fail at the handshake.
    /// </summary>
    public static Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Assertions.SetDefaultExpectTimeout(10_000);

        _playwright = await Playwright.CreateAsync();

        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = IsHeadless,
            SlowMo = IsHeadless ? 0 : 500
        });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}
