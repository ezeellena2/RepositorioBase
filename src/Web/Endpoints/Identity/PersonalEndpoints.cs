using CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;
using CleanArchitecture.Application.IdentityAccess.People.Profile;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// A person's own context over HTTP. None of these routes carries a subject: the anonymous one names an address it
/// must then prove, and the other three resolve the caller's own rows (IA-REQ-050).
/// </summary>
internal static class PersonalEndpoints
{
    /// <summary>
    /// The two answers a bounded route owes. They are declared together everywhere the claim budget is spent, so a
    /// client can tell "you tried too often" from "we are not answering right now".
    /// </summary>
    private static readonly ApiProblemContract[] BoundedClaim =
    [
        ApiProblemMetadata.RateLimitExceeded,
        ApiProblemMetadata.ServiceUnavailable
    ];

    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/personal/register", Register)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails([
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidRegistration,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PersonalRegistrationConflict,
                ApiProblemMetadata.InternalServerError,
                .. BoundedClaim])
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRegistration.Code);

        group.MapPost("/personal", Create)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails([
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidRegistration,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.PersonalRegistrationConflict,
                ApiProblemMetadata.InternalServerError,
                .. BoundedClaim])
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRegistration.Code);

        group.MapGet("/profile", GetProfile)
            .RequireAuthorization()
            .Produces<PersonalProfileResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.PersonalProfileNotFound,
                ApiProblemMetadata.InternalServerError);

        group.MapPut("/profile", UpdateProfile)
            .RequireAuthorization()
            .Produces<PersonalProfileResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.ProfileFieldNotEditable,
                ApiProblemMetadata.PersonalProfileNotFound,
                ApiProblemMetadata.PersonalProfileConcurrencyConflict,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ProfileFieldNotEditable.Code);
    }

    private static async Task<IResult> Register(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, RegisterPersonalCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Create(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, CreatePersonalContextCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> GetProfile(HttpContext context, ISender sender, ApiProblemDetailsMapper problems)
    {
        var result = await sender.Send(new GetPersonalProfileQuery(), context.RequestAborted);
        return result.IsSuccess ? Results.Ok(result.Value!) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> UpdateProfile(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, UpdatePersonalProfileCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.Ok(result.Value!) : problems.ToHttpResult(result.Error!);
    }
}
