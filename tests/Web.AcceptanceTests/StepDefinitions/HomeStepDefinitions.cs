namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class HomeStepDefinitions(HomePage homePage)
{
    private static IBrowserContext? featureContext;

    [BeforeFeature("Home")]
    public static async Task BeforeHomeFeature(IObjectContainer container)
    {
        var context = await PlaywrightSetup.NewContextAsync();
        featureContext = context;
        var page = await context.NewPageAsync();
        container.RegisterInstanceAs(context);
        container.RegisterInstanceAs(new HomePage(page));
    }

    [AfterFeature("Home")]
    public static async Task AfterHomeFeature()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
        }
    }

    [Given("a user visits the home page")]
    public Task GivenAUserVisitsTheHomePage() => homePage.GotoAsync();

    [Then("the header offers the link {string}")]
    public Task ThenTheHeaderOffersTheLink(string text) => homePage.AssertHeaderLink(text);

    [Then("the product navigation is not offered")]
    public Task ThenTheProductNavigationIsNotOffered() => homePage.AssertNoProductNavigation();

    [Then("the page carries no content")]
    public Task ThenThePageCarriesNoContent() => homePage.AssertNoPageContent();
}
