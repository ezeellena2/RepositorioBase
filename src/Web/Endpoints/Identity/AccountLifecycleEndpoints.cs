using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// Parking your own account and coming back (IA-REQ-054).
/// <para>
/// The two return routes are public because somebody whose account is parked cannot sign in to ask, which is the
/// same reason password recovery is public. Parking is not: it needs the session, and a password proved a moment
/// ago on top of it.
/// </para>
/// </summary>
internal static class AccountLifecycleEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/account/deactivate", Deactivate)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.RecentProofRequired,
                ApiProblemMetadata.LastAdministratorRequired,
                ApiProblemMetadata.PlatformLastOwner,
                ApiProblemMetadata.IdentityConcurrencyConflict,
                ApiProblemMetadata.RateLimitExceeded,
                ApiProblemMetadata.InternalServerError);

        group.MapPost("/account/reactivation-requests", RequestReturn)
            .RequireLoginAttemptBudgets()
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidRequest,
                ApiProblemMetadata.RateLimitExceeded,
                ApiProblemMetadata.ServiceUnavailable,
                ApiProblemMetadata.InternalServerError)
            .WithNeutralBodyBindingFailure(StatusCodes.Status202Accepted)
            .WithInvalidOptionalSessionRefusal();

        group.MapPost("/account/reactivate", Reactivate)
            .RequireLoginAttemptBudgets()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidReactivation,
                ApiProblemMetadata.RateLimitExceeded,
                ApiProblemMetadata.ServiceUnavailable,
                ApiProblemMetadata.InternalServerError)
            .WithInvalidOptionalSessionRefusal()
            // A body this route cannot read is one more way of not holding a usable ticket, and it answers like
            // every other one.
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidReactivation.Code);
    }

    private static async Task<IResult> Deactivate(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(new DeactivateAccountCommand(), context.RequestAborted);
        if (result.IsFailure) return problems.ToHttpResult(result.Error!);

        // The cookie is deleted rather than left to fail on its next use. Its session was revoked inside the
        // transaction, so it already opens nothing; this is only so the browser stops presenting it.
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        Identity.DeleteAntiforgeryCookie(context);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestReturn(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, RequestAccountReactivationCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);

        // Neutral either way: the same status for a parked address, a live one and one that does not exist.
        return result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Reactivate(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, ReactivateAccountCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);

        // No sign-in here, deliberately. Getting the account back and being signed into it are two things, and
        // the second one goes through the front door.
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }
}
