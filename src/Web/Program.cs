using CleanArchitecture.Web.Infrastructure.Identity;
using CleanArchitecture.Application.Common.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var defaultLanguage = LocalizationRegistry.RequireSupportedDefault(
    builder.Configuration["Localization:DefaultLanguage"]);
var supportedUiCultures = LocalizationRegistry.SupportedLanguages
    .Select(CultureInfo.GetCultureInfo)
    .ToList();

builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(defaultLanguage, defaultLanguage);
    options.SupportedUICultures = supportedUiCultures;
    // Server parsing and formatting stay invariant during Phase 1; only the UI culture is negotiated.
    options.SupportedCultures = [CultureInfo.GetCultureInfo(LocalizationRegistry.SourceLanguage)];
    options.FallBackToParentUICultures = true;
    options.FallBackToParentCultures = true;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Before everything that reads anything. A deployment that has not been admitted must not serve a file, read a
// cookie, look up a session or touch the restored database at all — deciding to refuse after consulting data
// that may itself be restored is the thing this guard exists to prevent (IA-REQ-055).
app.UseMiddleware<CleanArchitecture.Web.Infrastructure.Identity.RecoveryAdmissionMiddleware>();

// Above everything that writes a response, and below the admission guard, which answers before there is an
// application to protect. A header a route can forget is a header the next route will (IA-REQ-026/027).
app.UseIdentitySecurityHeaders();

app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "RepositorioBase API v1");
    });
}

app.UseExceptionHandler(options => { });
// Login partition keys must exist before the budgets are spent; only POST /api/identity/sessions carries the
// marker. Both run before authentication, so a refused attempt reaches no credential and no session.
app.UseLoginRateLimitKeys();
app.UseLoginAttemptBudgets();
app.UseAuthentication();
app.UseAuthorization();

#if (UseApiOnly)
app.Map("/", () => Results.Redirect("/scalar"));
#endif

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

#if (!UseApiOnly)
app.MapFallbackToFile("index.html");
#endif

app.Run();
