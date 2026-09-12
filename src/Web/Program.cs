using CleanArchitecture.Application.Common.Models;
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
app.UseExceptionHandler(options => { });
app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;
    if (context.Response.StatusCode != StatusCodes.Status404NotFound
        || !context.Request.Path.StartsWithSegments("/api"))
    {
        return;
    }

    var problems = context.RequestServices
        .GetRequiredService<CleanArchitecture.Web.Infrastructure.IProblemDetailsService>();
    var responseBody = context.Response.Body;
    if (HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.Body = Stream.Null;
    }

    try
    {
        await problems.WriteAsync(
            context,
            new ApplicationError(ApiProblemMetadata.NotFound.Code, ApplicationErrorCategory.NotFound),
            context.RequestAborted);
    }
    finally
    {
        context.Response.Body = responseBody;
    }
});
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

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "RepositorioBase API v1");
    });
}

#if (!UseApiOnly)
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var shouldUseSpaFallback = context.GetEndpoint() is null
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && !path.StartsWithSegments("/api")
        && IsNonFilePath(path);

    if (!shouldUseSpaFallback)
    {
        await next(context);
        return;
    }

    context.Request.Path = "/index.html";
    try
    {
        await next(context);
    }
    finally
    {
        context.Request.Path = path;
    }
});
#endif

app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

// Partition keys must exist before any endpoint carrying LoginAttemptBudgetMetadata spends its shared budgets.
// Both middleware run before authentication, so a budget refusal reaches no endpoint handler.
app.UseLoginRateLimitKeys();
app.UseLoginAttemptBudgets();
app.UseAuthentication();
app.UseAuthorization();
app.UseNeutralBodyBindingInvocation();

#if (UseApiOnly)
app.Map("/", () => Results.Redirect("/scalar"));
#endif

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

app.Run();

static bool IsNonFilePath(PathString path)
{
    var value = path.Value.AsSpan();
    var lastSegment = value[(value.LastIndexOf('/') + 1)..];
    var lastDot = lastSegment.LastIndexOf('.');
    return lastDot < 0 || lastDot == lastSegment.Length - 1;
}
