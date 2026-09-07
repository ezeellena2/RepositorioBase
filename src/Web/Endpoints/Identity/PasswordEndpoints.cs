using CleanArchitecture.Application.IdentityAccess.Credentials.ChangePassword;
using CleanArchitecture.Application.IdentityAccess.Credentials.OwnCredentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// Forgetting a password and changing one. The two recovery routes are public because somebody who cannot sign in
/// is the only person who needs them; the change is self-service and needs the proof (IA-REQ-051).
/// </summary>
internal static class PasswordEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/credentials/password/recovery", Recover)
            .RequireRateLimiting(LoginRateLimitPartitioner.PolicyName)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapPost("/credentials/password/reset", Reset)
            .Produces(StatusCodes.Status204NoContent)
            // `429` is newly reachable here: spending a link now waits for the same per-identity lock a sign-in
            // holds, and a wait that elapses is answered rather than forced through.
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidCredentialToken, ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidCredentialToken.Code);

        group.MapGet("/credentials", Read)
            .RequireAuthorization()
            .Produces<OwnCredentialsResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);

        group.MapPut("/credentials/password", Change)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);
    }

    private static async Task<IResult> Read(HttpContext context, ApiProblemDetailsMapper problems, ISender sender)
    {
        var result = await sender.Send(new GetOwnCredentialsQuery(), context.RequestAborted);
        return result.IsSuccess
            ? Results.Ok(new OwnCredentialsResponse(result.Value!.HasPassword, result.Value.PasswordUpdatedAt))
            : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Recover(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, RequestPasswordRecoveryCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);

        // Neutral either way: the same status for an address with an account and one without.
        return result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Reset(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, ResetPasswordCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);

        // No sign-in here, deliberately. Holding a mailed link is not the same as having signed in.
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Change(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, ChangePasswordCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        if (result.IsFailure) return problems.ToHttpResult(result.Error!);

        // The session that asked was rotated, so the caller is signed into the replacement in the same response
        // and the antiforgery pair is rotated with it — a new session inherits neither (IA-REQ-049).
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.Value!.IdentityId.ToString()),
            new Claim(ClaimTypes.Sid, result.Value.SessionId.ToString())
        };
        await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme)));
        Identity.DeleteAntiforgeryCookie(context);
        return Results.NoContent();
    }
}

/// <summary>Two members and no more: a hash, an address or a provider name here would be a leak.</summary>
public sealed record OwnCredentialsResponse(bool HasPassword, DateTimeOffset? PasswordUpdatedAt);
