namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class CounterStepDefinitions(CounterPage counterPage)
{
    private static IBrowserContext? featureContext;

    [BeforeFeature("Counter")]
    public static async Task BeforeCounterFeature(IObjectContainer container)
    {
        var context = await PlaywrightSetup.Browser.NewContextAsync();
        featureContext = context;
        var page = await context.NewPageAsync();
        container.RegisterInstanceAs(context);
        container.RegisterInstanceAs(new CounterPage(page));
    }

    [AfterFeature("Counter")]
    public static async Task AfterCounterFeature()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
        }
    }

    [Given("a user visits the counter page")]
    public Task GivenAUserVisitsTheCounterPage() => counterPage.GotoAsync();

    [Then("the counter heading is {string}")]
    public Task ThenTheCounterHeadingIs(string text) => counterPage.AssertHeading(text);

    [Then("the current count is {int}")]
    public Task ThenTheCurrentCountIs(int count) => counterPage.AssertCurrentCount(count);

    [When("the user clicks increment")]
    public Task WhenTheUserClicksIncrement() => counterPage.ClickIncrement();
}
