using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.Pages;

public sealed class LocalizationPage(IPage page) : BasePage(page)
{
    public override string PagePath => $"{BaseUrl}/";

    public async Task ChooseSpanishAsync()
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Language", Exact = true })
            .SelectOptionAsync(new SelectOptionValue { Label = "Español" });
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "es");
        var signIn = Page.GetByRole(AriaRole.Link, new() { Name = "Iniciar sesión", Exact = true });
        var register = Page.GetByRole(AriaRole.Link, new() { Name = "Registrarse", Exact = true });
        await Assertions.Expect(signIn).ToBeVisibleAsync();
        await Assertions.Expect(register).ToBeVisibleAsync();
        ILocator[] toolbarControls =
        [
            Page.GetByRole(AriaRole.Link, new() { Name = "Clean Architecture", Exact = true }),
            Page.GetByRole(AriaRole.Combobox, new() { Name = "Idioma", Exact = true }),
            signIn,
            register
        ];
        foreach (var control in toolbarControls)
        {
            await Assertions.Expect(control).ToBeVisibleAsync();
            (await control.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return box.left >= 0 && box.top >= 0 && box.right <= window.innerWidth && box.bottom <= window.innerHeight; }"))
                .ShouldBeTrue("Each Spanish toolbar control must fit within the narrow viewport after anonymous bootstrap.");
        }
        (await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .ShouldBeTrue("The Spanish shell must fit a narrow viewport.");
        await signIn.ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToHaveTextAsync("Iniciar sesión");
    }

    public async Task SignInSpanishAsync(string email, string password)
    {
        await Page.GetByLabel("Correo electrónico", new() { Exact = true }).FillAsync(email);
        await Page.GetByLabel("Contraseña", new() { Exact = true }).FillAsync(password);
        var response = await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Iniciar sesión", Exact = true }).ClickAsync(),
            candidate => candidate.Url.EndsWith("/api/identity/sessions", StringComparison.Ordinal)
                         && candidate.Request.Method == "POST");
        response.Status.ShouldBe(204);
        await Assertions.Expect(Page).ToHaveURLAsync($"{BaseUrl}/identity");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Su acceso");
    }

    public async Task OpenAccessAgainAsync()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/identity");
        response.ShouldNotBeNull();
        response.Ok.ShouldBeTrue();
    }

    public async Task AssertSpanishAccessAsync()
    {
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "es");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Su acceso");
        await Assertions.Expect(Page.GetByText("ninguno seleccionado", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Combobox, new() { Name = "Idioma", Exact = true })).ToHaveValueAsync("es");
        await Assertions.Expect(Page.Locator("body")).Not.ToContainTextAsync(
            new Regex(@"\b(?:common|identity|platform|errors|enums):[\w.]+|\b(?:navigation|context|login|permissions|roles\.system)\.[\w.]+"));
    }
}
