namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class LoginStepDefinitions(LoginPage loginPage)
{
    private static IBrowserContext? featureContext;

    [BeforeFeature("Login")]
    public static async Task BeforeLoginFeature(IObjectContainer container)
    {
        var context = await PlaywrightSetup.Browser.NewContextAsync();
        featureContext = context;
        var page = await context.NewPageAsync();
        container.RegisterInstanceAs(context);
        container.RegisterInstanceAs(new LoginPage(page));
    }

    [AfterFeature("Login")]
    public static async Task AfterLoginFeature()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
        }
    }

    [Given("a logged out user")]
    public async Task GivenALoggedOutUser()
    {
        await loginPage.ClearAuthenticationAsync();
        await loginPage.GotoAsync();
    }

    [When("the user logs in with valid credentials")]
    public async Task TheUserLogsInWithValidCredentials()
    {
        await AcceptanceTestCredentials.SignInAsync(loginPage);
    }

    [Then("they log in successfully")]
    public Task TheyLogInSuccessfully() => AcceptanceTestCredentials.AssertAuthenticatedAsync(loginPage);

    [When("the user logs in with invalid credentials")]
    public Task TheUserLogsInWithInvalidCredentials() => AcceptanceTestCredentials.SignInWithInvalidCredentialsAsync(loginPage);

    [Then("no authenticated session is established")]
    public async Task NoAuthenticatedSessionIsEstablished()
    {
        (await loginPage.HasAuthenticationCookieAsync()).ShouldBeFalse();
    }
}
