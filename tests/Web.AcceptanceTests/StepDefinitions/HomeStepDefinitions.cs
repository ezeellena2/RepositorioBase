namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class HomeStepDefinitions(HomePage homePage)
{
    private static IBrowserContext? featureContext;

    [BeforeFeature("Home")]
    public static async Task BeforeHomeFeature(IObjectContainer container)
    {
        var context = await PlaywrightSetup.Browser.NewContextAsync();
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

    [Then("the heading {string} is visible")]
    public Task ThenTheHeadingIsVisible(string text) => homePage.AssertHeading(text);
}
