using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using CleanArchitecture.Web.IdentityEndpoints.Contracts;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// The invitation routes of SPEC section 6. Issuing, reissuing and withdrawing are addressed under their tenant;
/// registering and accepting are not, because an invitee has no tenant context until acceptance succeeds.
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

        // Reissuing and withdrawing carry no body: the invitation is named by the route and everything else about
        // the offer is already recorded. They answer 204 because there is nothing new to hand back — a reissue
        // mints a token for the recipient's envelope, never for the caller (IA-REQ-015/017/018).
        MapOffer(group.MapPost("/{tenantId:guid}/invitations/{invitationId:guid}/resend", Resend));
        MapOffer(group.MapPost("/{tenantId:guid}/invitations/{invitationId:guid}/cancel", Cancel));
    }

    /// <summary>Reissuing and withdrawing differ in permission and effect, never in contract.</summary>
    private static void MapOffer(RouteHandlerBuilder route) =>
        route.RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.InvalidInvitation,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.InvitationConflict,
                ApiProblemMetadata.InternalServerError);

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

    /// <summary>
    /// The route tenant is carried into the command and compared there against the session's active tenant; it is
    /// never trusted on its own. The <c>Location</c> names the created invitation by identifier, which is the only
    /// thing about it a caller may hold — the usable token reaches the recipient through the encrypted envelope and
    /// nothing else (IA-REQ-015/018).
    /// </summary>
    private static async Task<IResult> Issue(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid tenantId,
        InviteMemberRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        // The strongly-typed identifier refuses an empty value by throwing, which the caller would read as a
        // sanitized 500. An empty route tenant is decidable from the request alone, so it is refused as input.
        if (tenantId == Guid.Empty) return problems.ToHttpResult(IdentityAccessErrors.InvalidInvitation());

        var result = await sender.Send(
            new InviteMemberCommand(TenantId.From(tenantId), request.Email, request.RoleIds ?? []),
            context.RequestAborted);

        return result.ToHttpResult(context, problems, issued => Results.Created(
            $"/api/tenants/{tenantId}/invitations/{issued.InvitationId}",
            new InvitationCreatedResponse(issued.InvitationId, issued.ExpiresAt)));
    }

    private static Task<IResult> Resend(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid invitationId) =>
        OfferAsync(context, antiforgery, problems, tenantId, () => sender.Send(new ResendInvitationCommand(TenantId.From(tenantId), invitationId), context.RequestAborted));

    private static Task<IResult> Cancel(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid invitationId) =>
        OfferAsync(context, antiforgery, problems, tenantId, () => sender.Send(new CancelInvitationCommand(TenantId.From(tenantId), invitationId), context.RequestAborted));

    private static async Task<IResult> OfferAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        Guid tenantId,
        Func<Task<Result>> send)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (tenantId == Guid.Empty) return problems.ToHttpResult(IdentityAccessErrors.InvalidInvitation());

        var result = await send();
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    /// <summary>
    /// Neutral by contract: a bodyless <c>202</c> whether the token was live, dead, or already belonged to an
    /// account. The only failure it can answer with is one decided from the request alone (SPEC section 6).
    /// </summary>
    private static async Task<IResult> Register(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        RegisterInvitedUserRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new RegisterInvitedUserCommand(request.Token, request.Password), context.RequestAborted);
        return result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : problems.ToHttpResult(result.Error!);
    }

    /// <summary>Idempotent by contract: a replay by the accepting identity answers with the same membership.</summary>
    private static async Task<IResult> Accept(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        AcceptInvitationRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new AcceptInvitationCommand(request.Token), context.RequestAborted);
        return result.ToHttpResult(context, problems, accepted =>
            Results.Ok(new InvitationAcceptanceResponse(accepted.TenantId, accepted.MembershipId)));
    }

    internal sealed record InviteMemberRequest(string Email, IReadOnlyList<Guid> RoleIds);

    internal sealed record RegisterInvitedUserRequest(string Token, string Password);

    internal sealed record AcceptInvitationRequest(string Token);
}
