using System.Security.Claims;
using System.Text.Encodings.Web;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>Test-only authentication, never part of the production session contract.</summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FunctionalTest";
    public const string PermissionClaim = "task6.permission";
    public const string PermissionValue = "granted";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = TestApp.GetUserId();
        if (userId is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.Value.ToString()) };
        if (TestApp.GetSessionId() is { } sessionId)
        {
            claims.Add(new Claim(ClaimTypes.Sid, sessionId.ToString()));
        }
        if (TestApp.IsHttpAuthorizationGranted())
        {
            claims.Add(new Claim(PermissionClaim, PermissionValue));
        }
        if (TestApp.IsApplicationPermissionGranted())
        {
            claims.Add(new Claim(Permissions.ApplicationPermissionClaimType, Permissions.TodosRead));
            claims.Add(new Claim(Permissions.ApplicationPermissionClaimType, Permissions.TodosWrite));
            claims.Add(new Claim(Permissions.ApplicationPermissionClaimType, Permissions.WeatherRead));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = Scheme.Name;
        return Task.CompletedTask;
    }
}
