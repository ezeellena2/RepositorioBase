using CleanArchitecture.Web.Infrastructure.Identity;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler(options => { });
// Login partition keys must exist before the limiter runs; only POST /api/identity/sessions carries a policy.
app.UseLoginRateLimitKeys();
app.UseRateLimiter();
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
