using CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;
using CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;
using CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions;
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
        // The only rate-limited route: chained client-address and normalized-account budgets over the shared
        // store (IA-REQ-019). It declares both refusals, because an unreachable store answers `503` and not `429`.
        group.MapPost("/sessions", Create)
            .RequireLoginAttemptBudgets()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.CredentialSuperseded, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.ServiceUnavailable, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);
        group.MapGet("/sessions", List)
            .RequireAuthorization()
            .Produces<IReadOnlyList<OwnSessionResponse>>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);

        // The literal routes are declared before the parameterised one so `current` and `others` keep their own
        // contracts; ASP.NET prefers a literal segment anyway, and stating it here keeps that from being luck.
        group.MapDelete("/sessions/current", Revoke)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.SessionConcurrencyConflict, ApiProblemMetadata.InternalServerError);

        group.MapDelete("/sessions/others", RevokeOthers)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError);

        group.MapDelete("/sessions/{sessionRef}", RevokeOne)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.SessionNotFound, ApiProblemMetadata.SessionConcurrencyConflict, ApiProblemMetadata.InternalServerError);

        group.MapPost("/credentials/reauthenticate", Reauthenticate)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.EmailConfirmationRequired, ApiProblemMetadata.InvalidCredentialProof, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidCredentialProof.Code);
    }

    private static async Task<IResult> List(HttpContext context, ApiProblemDetailsMapper problems, ISender sender)
    {
        var result = await sender.Send(new ListOwnSessionsQuery(), context.RequestAborted);
        return result.IsSuccess ? Results.Ok(result.Value!) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> RevokeOne(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, string sessionRef)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(new RevokeOwnSessionCommand(sessionRef), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> RevokeOthers(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(new RevokeOtherSessionsCommand(), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Reauthenticate(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, ReauthenticateCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Create(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, CreateSessionCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        if (result.IsFailure)
        {
            // Neutral for everything a stranger can provoke — a wrong password, an unknown address, an
            // unconfirmed account — and explicit for the one refusal only a valid credential can reach: the
            // credential was replaced while this very sign-in was being checked, so the session it would have
            // issued must not exist and the caller is told so rather than handed a cookie (C2/C4).
            return result.Error!.Code == ApiProblemMetadata.CredentialSuperseded.Code
                ? problems.ToHttpResult(result.Error)
                : Results.NoContent();
        }

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
