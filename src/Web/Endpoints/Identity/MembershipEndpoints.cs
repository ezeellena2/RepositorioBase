using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Members;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// Member administration and ownership inside one `Organization` (IA-REQ-053).
/// <para>
/// The three status routes are separate paths rather than one with a state parameter, because they are three
/// different things to ask for and a reader of the route table should be able to see which one a caller used.
/// Changing which roles a member holds and transferring ownership each spend a recent identity proof (amendment
/// D2); suspending, reactivating and revoking do not, and echo the row's own `version` instead.
/// </para>
/// </summary>
internal static class MembershipEndpoints
{
    internal static void MapTenantScoped(RouteGroupBuilder group)
    {
        group.MapGet("/{tenantId:guid}/members", ListMembers)
            .RequireAuthorization()
            .Produces<MemberPageResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapGet("/{tenantId:guid}/invitations", ListInvitations)
            .RequireAuthorization()
            .Produces<InvitationSummaryPageResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapPut("/{tenantId:guid}/members/{membershipId:guid}/roles", UpdateRoles)
            .RequireAuthorization()
            .Produces<MemberResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.InvalidMembershipOperation, ApiProblemMetadata.NotFound, ApiProblemMetadata.MembershipConcurrencyConflict, ApiProblemMetadata.LastAdministratorRequired, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidMembershipOperation.Code);

        MapStatus(group.MapPost("/{tenantId:guid}/members/{membershipId:guid}/suspend", Suspend));
        MapStatus(group.MapPost("/{tenantId:guid}/members/{membershipId:guid}/reactivate", Reactivate));
        MapStatus(group.MapPost("/{tenantId:guid}/members/{membershipId:guid}/revoke", Revoke));

        group.MapPost("/{tenantId:guid}/ownership/transfer", Transfer)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.OwnerRequired, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.InvalidMembershipOperation, ApiProblemMetadata.NotFound, ApiProblemMetadata.MembershipConcurrencyConflict, ApiProblemMetadata.LastAdministratorRequired, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidMembershipOperation.Code);
    }

    /// <summary>The three status routes differ only in the state they ask for, so they share their contract.</summary>
    private static void MapStatus(RouteHandlerBuilder route) =>
        route.RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InvalidMembershipOperation, ApiProblemMetadata.NotFound, ApiProblemMetadata.MembershipConcurrencyConflict, ApiProblemMetadata.LastAdministratorRequired, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidMembershipOperation.Code);

    private static Task<IResult> Suspend(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid membershipId, MemberStatusRequest body) =>
        ChangeStatusAsync(context, antiforgery, problems, sender, tenantId, membershipId, MemberStatusChange.Suspend, body);

    private static Task<IResult> Reactivate(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid membershipId, MemberStatusRequest body) =>
        ChangeStatusAsync(context, antiforgery, problems, sender, tenantId, membershipId, MemberStatusChange.Reactivate, body);

    private static Task<IResult> Revoke(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid membershipId, MemberStatusRequest body) =>
        ChangeStatusAsync(context, antiforgery, problems, sender, tenantId, membershipId, MemberStatusChange.Revoke, body);

    private static async Task<IResult> ChangeStatusAsync(
        HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid membershipId, MemberStatusChange change, MemberStatusRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new ChangeMemberStatusCommand(TenantId.From(tenantId), membershipId, change, body.Version), context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static async Task<IResult> ListMembers(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListMembersQuery(TenantId.From(tenantId), PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(MemberPageResponse.From(page, Describe)));
    }

    private static async Task<IResult> ListInvitations(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListTenantInvitationsQuery(TenantId.From(tenantId), PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(InvitationSummaryPageResponse.From(page, item =>
            new InvitationSummaryResponse(item.InvitationId, item.NormalizedEmail, item.Status, item.CreatedAt, item.ExpiresAt, item.RoleIds))));
    }

    private static async Task<IResult> UpdateRoles(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid membershipId, UpdateMemberRolesRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new UpdateMemberRolesCommand(TenantId.From(tenantId), membershipId, body.RoleIds, body.Version), context.RequestAborted);
        return result.ToHttpResult(context, problems, member => Results.Ok(Describe(member)));
    }

    private static async Task<IResult> Transfer(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, TransferOwnershipRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new TransferOwnershipCommand(TenantId.From(tenantId), body.ToMembershipId, body.Version), context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static MemberResponse Describe(MemberView member) =>
        new(member.MembershipId, member.IdentityId, member.DisplayName, member.NormalizedEmail, member.Status, member.RoleIds, member.IsOwner, member.Version);
}

public sealed record MemberResponse(
    Guid MembershipId,
    Guid IdentityId,
    string DisplayName,
    string NormalizedEmail,
    string Status,
    IReadOnlyList<Guid> RoleIds,
    bool IsOwner,
    string Version);

public sealed record MemberPageResponse(
    IReadOnlyList<MemberResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static MemberPageResponse From(PaginatedList<MemberView> page, Func<MemberView, MemberResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record InvitationSummaryResponse(
    Guid InvitationId,
    string NormalizedEmail,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Guid> RoleIds);

public sealed record InvitationSummaryPageResponse(
    IReadOnlyList<InvitationSummaryResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static InvitationSummaryPageResponse From(
        PaginatedList<InvitationSummaryView> page, Func<InvitationSummaryView, InvitationSummaryResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record UpdateMemberRolesRequest(IReadOnlyList<Guid>? RoleIds, string? Version);

/// <summary>
/// The one precondition a status change carries. C5 and C6 spelled it two ways — `version` and `expectedStatus`;
/// C5 owns the definition, so the row's own concurrency token is what stands, and it does the same job: a caller
/// acting on a member they have not looked at since somebody else changed them is refused.
/// </summary>
public sealed record MemberStatusRequest(string? Version);

public sealed record TransferOwnershipRequest(Guid ToMembershipId, string? Version);
