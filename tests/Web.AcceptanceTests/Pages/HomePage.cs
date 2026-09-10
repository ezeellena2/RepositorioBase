namespace CleanArchitecture.Web.AcceptanceTests.Pages;

/// <summary>
/// The front door. It carries no content of its own, so what it is addressed by is the bar above it: the two ways
/// into the product, and the absence of the product navigation beside them.
/// </summary>
public class HomePage(IPage page) : BasePage(page)
{
    public override string PagePath => BaseUrl;

    /// <summary>
    /// Looked up inside <c>banner</c> rather than on the page, because where the link is placed is the point.
    /// Finding it anywhere would pass just as well with the sidebar still standing there.
    /// </summary>
    public Task AssertHeaderLink(string text)
        => Assertions.Expect(Page.GetByRole(AriaRole.Banner).GetByRole(AriaRole.Link, new() { Name = text })).ToBeVisibleAsync();

    /// <summary>The product navigation is what a session opens, so a visitor is not offered it.</summary>
    public Task AssertNoProductNavigation()
        => Assertions.Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Primary navigation" })).ToHaveCountAsync(0);

    /// <summary>
    /// Blank, for now. Asserted as the absence of a heading rather than of markup, because the shell still puts a
    /// spacer and a container in <c>main</c> to keep whatever lands there clear of the fixed bar.
    /// </summary>
    public Task AssertNoPageContent()
        => Assertions.Expect(Page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Heading)).ToHaveCountAsync(0);
}
