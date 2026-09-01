using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace CleanArchitecture.Web.Infrastructure;

public sealed class ApiAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await ApiAuthenticationChallenge.ChallengeAsync(context);
            await WriteDeniedAsync(context, new ApplicationError("authentication_required", ApplicationErrorCategory.Authentication), "identity_missing_or_invalid");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteDeniedAsync(context, new ApplicationError("permission_denied", ApplicationErrorCategory.Authorization), "permission_denied");
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteDeniedAsync(HttpContext context, ApplicationError error, string outcome)
    {
        var auditWriter = context.RequestServices.GetRequiredService<ISecurityDenialAuditWriter>();
        await auditWriter.WriteDeniedAsync(new SecurityDenialAudit(
            System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            GetGuidClaim(context.User, ClaimTypes.NameIdentifier),
            null,
            "endpoint.authorization",
            outcome,
            GetGuidClaim(context.User, ClaimTypes.Sid)), CancellationToken.None);
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(context, error, context.RequestAborted);
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) && value != Guid.Empty ? value : null;
}

internal static class ApiAuthenticationChallenge
{
    public static async Task ChallengeAsync(HttpContext context)
    {
        await context.ChallengeAsync();

        if (!context.Response.HasStarted && context.Response.StatusCode is >= StatusCodes.Status300MultipleChoices and < StatusCodes.Status400BadRequest)
        {
            context.Response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
        }
    }
}
