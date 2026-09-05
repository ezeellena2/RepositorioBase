using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.PlatformEndpoints.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.PlatformEndpoints;

/// <summary>
/// The two routes a Platform invitation's recipient reaches before they hold any authority (SPEC section 6). Both
/// are public and antiforgery-protected, and neither can grant a membership.
/// </summary>
internal static class PlatformInvitationEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/invitations/register", Register)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.InvalidInvitation,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/invitations/confirm", Confirm)
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.InvalidConfirmation,
                ApiProblemMetadata.RegistrationConflict,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidConfirmation.Code);
    }

    /// <summary>
    /// Neutral by contract: a bodyless <c>202</c> whether the token was live, dead, or already belonged to an
    /// account. The only failures it can answer with are ones decided from the request alone.
    /// </summary>
    private static async Task<IResult> Register(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformInvitationRegistrationRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(
            new RegisterPlatformInviteeCommand(request.Token, request.Password),
            context.RequestAborted);

        return result.IsSuccess
            ? Results.StatusCode(StatusCodes.Status202Accepted)
            : problems.ToHttpResult(result.Error!);
    }

    /// <summary>Idempotent by contract: replaying a consumed confirmation answers with the same bodyless 204.</summary>
    private static async Task<IResult> Confirm(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformInvitationConfirmationRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(
            new ConfirmPlatformInviteeCommand(request.ConfirmationToken),
            context.RequestAborted);

        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }
}
