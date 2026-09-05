namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class WeatherStepDefinitions(WeatherPage weatherPage)
{
    private static IBrowserContext? featureContext;

    [BeforeFeature("Weather")]
    public static async Task BeforeWeatherFeature(IObjectContainer container)
    {
        var context = await PlaywrightSetup.NewContextAsync();
        featureContext = context;
        var page = await context.NewPageAsync();

        var loginPage = new LoginPage(page);
        await loginPage.GotoAsync();
        await AcceptanceTestCredentials.SignInAsync(loginPage);

        container.RegisterInstanceAs(context);
        container.RegisterInstanceAs(new WeatherPage(page));
    }

    [AfterFeature("Weather")]
    public static async Task AfterWeatherFeature()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
        }
    }

    [Given("an authenticated user visits the weather page")]
    public Task GivenAnAuthenticatedUserVisitsTheWeatherPage() => weatherPage.GotoAsync();

    [Then("the weather forecast heading is {string}")]
    public Task ThenTheWeatherForecastHeadingIs(string text) => weatherPage.AssertHeading(text);

    [Then("the weather forecast table is displayed")]
    public Task ThenTheWeatherForecastTableIsDisplayed() => weatherPage.AssertTableVisible();

    [Then("{int} weather forecasts are shown")]
    public Task ThenWeatherForecastsAreShown(int count) => weatherPage.AssertRowCount(count);
}
