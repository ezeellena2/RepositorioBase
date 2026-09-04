using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using CleanArchitecture.Web.IdentityEndpoints.Contracts;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// The three invitation routes of SPEC section 6. Issuing is addressed under its tenant; registering and
/// accepting are not, because an invitee has no tenant context until acceptance succeeds.
/// </summary>
internal static class InvitationEndpoints
{
    internal static void MapTenantScoped(RouteGroupBuilder group)
    {
        group.MapPost("/{tenantId:guid}/invitations", Issue)
            .RequireAuthorization()
            .WithCreatedLocation<InvitationCreatedResponse>()
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidInvitation,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.NotFound,
                ApiProblemMetadata.InvitationConflict,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);
    }

    internal static void MapPublic(RouteGroupBuilder group)
    {
        group.MapPost("/register", Register)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidInvitation,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);
        group.MapPost("/accept", Accept)
            .RequireAuthorization()
            .Produces<InvitationAcceptanceResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidInvitation,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.InvitationConflict,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);
    }

    private static Task<IResult> Issue(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid tenantId,
        InviteMemberRequest request) => throw new NotImplementedException();

    private static Task<IResult> Register(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        RegisterInvitedUserRequest request) => throw new NotImplementedException();

    private static Task<IResult> Accept(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        AcceptInvitationRequest request) => throw new NotImplementedException();

    internal sealed record InviteMemberRequest(string Email, IReadOnlyList<Guid> RoleIds);

    internal sealed record RegisterInvitedUserRequest(string Token, string Password);

    internal sealed record AcceptInvitationRequest(string Token);
}
