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
    /// <para>
    /// Each context also arrives from an address of its own. Every browser in this run reaches the application
    /// through one loopback proxy, so without this they are one client — and the sign-in budget is twenty
    /// attempts per five minutes for a client, which a suite of journeys spends in a couple of minutes. The
    /// journeys are different people on different machines, and this is what says so. It is documentary, not a
    /// bypass: the budget is real, it is spent, and the account budget every scenario also meets is untouched.
    /// </para>
    /// </summary>
    public static Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Forwarded-For"] = NextClientAddress() }
        });

    /// <summary>An address out of TEST-NET-3 (RFC 5737), which exists precisely so documentation can name one.</summary>
    private static string NextClientAddress()
    {
        var ordinal = Interlocked.Increment(ref _clients);
        return $"203.0.113.{ordinal % 200 + 1}";
    }

    private static int _clients;

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
