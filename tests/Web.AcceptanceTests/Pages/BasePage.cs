namespace CleanArchitecture.Web.AcceptanceTests.Pages;

public abstract class BasePage(IPage page)
{
    protected static string BaseUrl => AspireSetup.App.GetEndpoint(Services.WebFrontend).ToString().TrimEnd('/');

    public abstract string PagePath { get; }

    protected IPage Page { get; } = page;

    public async Task GotoAsync()
    {
        var response = await Page.GotoAsync(PagePath);
        response.ShouldNotBeNull();
        response.Ok.ShouldBeTrue($"Navigation to {PagePath} returned HTTP {response.Status}.");
    }
}
