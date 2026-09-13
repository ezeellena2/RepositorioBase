using System.Text.RegularExpressions;
using System.Text.Json;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

public sealed class LocalizationPage(IPage page) : BasePage(page)
{
    private LocalizationText text = null!;

    public override string PagePath => $"{BaseUrl}/";

    public async Task ChooseLanguageAsync(string language)
    {
        text = LocalizationText.Load(language);
        await Page.GetByRole(AriaRole.Combobox, new() { Name = text.SourceLanguageLabel, Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = text.TargetAutonym, Exact = true }).ClickAsync();
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", text.Language);
        var signIn = Page.GetByRole(AriaRole.Link, new() { Name = text.SignInLink, Exact = true });
        var register = Page.GetByRole(AriaRole.Link, new() { Name = text.RegisterLink, Exact = true });
        await Assertions.Expect(signIn).ToBeVisibleAsync();
        await Assertions.Expect(register).ToBeVisibleAsync();
        ILocator[] toolbarControls =
        [
            Page.GetByRole(AriaRole.Link, new() { Name = text.Brand, Exact = true }),
            Page.GetByRole(AriaRole.Combobox, new() { Name = text.TargetLanguageLabel, Exact = true }),
            signIn,
            register
        ];
        foreach (var control in toolbarControls)
        {
            await Assertions.Expect(control).ToBeVisibleAsync();
            (await control.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return box.left >= 0 && box.top >= 0 && box.right <= window.innerWidth && box.bottom <= window.innerHeight; }"))
                .ShouldBeTrue("Each localized toolbar control must fit within the narrow viewport after anonymous bootstrap.");
        }
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .ShouldBeTrue("The localized shell must fit a narrow viewport.");
        await signIn.ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToHaveTextAsync(text.LoginTitle);
    }

    public async Task SignInAsync(string email, string password)
    {
        await Page.GetByLabel(text.EmailLabel, new() { Exact = true }).FillAsync(email);
        await Page.GetByLabel(text.PasswordLabel, new() { Exact = true }).FillAsync(password);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = text.SignInButton, Exact = true }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/sessions", StringComparison.Ordinal)
                         && candidate.Request.Method == "POST");
        response.Status.ShouldBe(204);
        await Assertions.Expect(Page).ToHaveURLAsync($"{BaseUrl}/identity");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(text.AccessTitle);
    }

    public async Task OpenAccessAgainAsync()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/identity");
        response.ShouldNotBeNull();
        response.Ok.ShouldBeTrue();
    }

    public async Task AssertLocalizedAccessAsync()
    {
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", text.Language);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(text.AccessTitle);
        await Assertions.Expect(Page.GetByText(text.NoneSelected, new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Combobox, new() { Name = text.TargetLanguageLabel, Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("input[name='language']")).ToHaveValueAsync(text.Language);
        await Assertions.Expect(Page.Locator("body")).Not.ToContainTextAsync(
            new Regex(@"\b(?:common|identity|platform|errors|enums):[\w.]+|\b(?:navigation|context|login|permissions|roles\.system)\.[\w.]+"));
    }

    private sealed record LocalizationText(
        string Language,
        string SourceLanguageLabel,
        string TargetAutonym,
        string TargetLanguageLabel,
        string Brand,
        string SignInLink,
        string RegisterLink,
        string LoginTitle,
        string EmailLabel,
        string PasswordLabel,
        string SignInButton,
        string AccessTitle,
        string NoneSelected)
    {
        public static LocalizationText Load(string language)
        {
            using var registry = ReadJson("src/Web/ClientApp/src/i18n/languages.json");
            var sourceLanguage = registry.RootElement.GetProperty("source").GetString().ShouldNotBeNull();
            using var sourceCommon = ReadJson($"src/Web/ClientApp/src/i18n/locales/{sourceLanguage}/common.json");
            using var targetCommon = ReadJson($"src/Web/ClientApp/src/i18n/locales/{language}/common.json");
            using var targetIdentity = ReadJson($"src/Web/ClientApp/src/i18n/locales/{language}/identity.json");

            return new LocalizationText(
                language,
                Value(sourceCommon.RootElement, "language", "label"),
                Value(sourceCommon.RootElement, "language", language),
                Value(targetCommon.RootElement, "language", "label"),
                Value(targetCommon.RootElement, "navigation", "brand"),
                Value(targetCommon.RootElement, "navigation", "logIn"),
                Value(targetCommon.RootElement, "navigation", "register"),
                Value(targetIdentity.RootElement, "login", "title"),
                Value(targetIdentity.RootElement, "login", "email"),
                Value(targetIdentity.RootElement, "login", "password"),
                Value(targetIdentity.RootElement, "login", "submit"),
                Value(targetCommon.RootElement, "navigation", "yourAccess"),
                Value(targetIdentity.RootElement, "context", "noneSelected"));
        }

        private static JsonDocument ReadJson(string relativePath) =>
            JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(relativePath)));

        private static string Value(JsonElement root, params string[] path)
        {
            var value = path.Aggregate(root, (current, segment) => current.GetProperty(segment));
            return value.GetString().ShouldNotBeNull();
        }

        private static string GetRepositoryPath(string relativePath) =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
    }
}
