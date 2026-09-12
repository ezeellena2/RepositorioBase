namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

[Binding]
public sealed class LocalizationStepDefinitions
{
    private IBrowserContext? context;
    private LocalizationPage page = null!;
    private IdentityAccessFixtures.SeededIdentity identity = null!;

    [BeforeScenario("Localization")]
    public async Task BeforeLocalizationScenario()
    {
        context = await PlaywrightSetup.NewContextAsync();
        var browserPage = await context.NewPageAsync();
        await browserPage.SetViewportSizeAsync(375, 812);
        page = new LocalizationPage(browserPage);
    }

    [AfterScenario("Localization")]
    public async Task AfterLocalizationScenario()
    {
        if (context is not null) await context.DisposeAsync();
    }

    [Given("a confirmed identity opens the anonymous localization shell")]
    public async Task GivenTheLocalizationVisitor()
    {
        identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        await page.GotoAsync();
    }

    [When("the visitor chooses (.*) and signs in through the localized form")]
    public async Task WhenTheVisitorChoosesLanguage(string language)
    {
        await page.ChooseLanguageAsync(language);
        await page.SignInAsync(identity.Email, IdentityAccessFixtures.Password);
    }

    [Then("the localized access page survives a full-page navigation without raw catalog keys")]
    public async Task ThenTheLanguagePersists()
    {
        await page.OpenAccessAgainAsync();
        await page.AssertLocalizedAccessAsync();
    }
}
