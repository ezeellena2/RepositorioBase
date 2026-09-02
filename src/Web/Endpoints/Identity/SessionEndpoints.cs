using CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;
using CleanArchitecture.Application.IdentityAccess.Sessions.RevokeCurrentSession;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace CleanArchitecture.Web.IdentityEndpoints;

internal static class SessionEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        // The only rate-limited route: chained client-address and normalized-account transport partitions (IA-REQ-019).
        group.MapPost("/sessions", Create)
            .RequireRateLimiting(LoginRateLimitPartitioner.PolicyName)
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);
        group.MapDelete("/sessions/current", Revoke)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.SessionConcurrencyConflict, ApiProblemMetadata.InternalServerError);
    }

    private static async Task<IResult> Create(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, CreateSessionCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        if (result.IsFailure) return Results.NoContent();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.Value!.IdentityId.ToString()),
            new Claim(ClaimTypes.Sid, result.Value.SessionId.ToString())
        };
        await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme)));

        // Antiforgery rotation on sign-in: the pre-authentication pair is deleted here and every later token is
        // bound to the validated session, so the client must bootstrap a fresh pair and old pairs are rejected.
        Identity.DeleteAntiforgeryCookie(context);
        return Results.NoContent();
    }

    private static async Task<IResult> Revoke(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new RevokeCurrentSessionCommand(), context.RequestAborted);
        if (result.IsFailure)
        {
            // A session that another request already revoked, or that expired while this one ran, passed cookie
            // validation but is dead now. The caller asked to sign out, so the useless cookie is deleted anyway;
            // only a still-live session that lost its write keeps its cookie for the retry the conflict invites.
            if (result.Error!.Code == ApiProblemMetadata.InvalidSession.Code)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                Identity.DeleteAntiforgeryCookie(context);
            }

            return problems.ToHttpResult(result.Error);
        }

        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        Identity.DeleteAntiforgeryCookie(context);
        return Results.NoContent();
    }
}
