using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitecture.Application.IdentityAccess.Common;
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
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/personal/register", Register)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails([
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.InvalidRegistration,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PersonalRegistrationConflict,
                ApiProblemMetadata.InternalServerError])
            .WithInvalidOptionalSessionRefusal()
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);

        group.MapPost("/personal", Create)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails([
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.PersonalRegistrationConflict,
                ApiProblemMetadata.InternalServerError,
                .. ApiProblemMetadata.BoundedAttempt])
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);

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
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.ProfileFieldNotEditable,
                ApiProblemMetadata.PersonalProfileNotFound,
                ApiProblemMetadata.PersonalProfileConcurrencyConflict,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);
    }

    private static async Task<IResult> Register(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, RegisterPersonalCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.ToAcceptedHttpResult(context, problems);
    }

    private static async Task<IResult> Create(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, CreatePersonalContextCommand command)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static async Task<IResult> GetProfile(HttpContext context, ISender sender, ApiProblemDetailsMapper problems)
    {
        var result = await sender.Send(new GetPersonalProfileQuery(), context.RequestAborted);
        return result.ToHttpResult(context, problems, profile => Results.Ok(profile));
    }

    private static async Task<IResult> UpdateProfile(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, UpdatePersonalProfileRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var unknownMember = request.AdditionalMembers?.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
        if (unknownMember is not null)
            return problems.ToHttpResult(IdentityAccessErrors.ProfileFieldNotEditable(unknownMember));

        var command = new UpdatePersonalProfileCommand(request.FullName!, request.DisplayName!, request.Version!);
        var result = await sender.Send(command, context.RequestAborted);
        return result.ToHttpResult(context, problems, profile => Results.Ok(profile));
    }

    private sealed class UpdatePersonalProfileRequest
    {
        public string? FullName { get; init; }
        public string? DisplayName { get; init; }
        public string? Version { get; init; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement>? AdditionalMembers { get; init; }
    }
}
