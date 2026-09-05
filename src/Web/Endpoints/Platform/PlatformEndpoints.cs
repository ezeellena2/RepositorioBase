using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Application.IdentityAccess.Platform.Organizations;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.PlatformEndpoints.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.PlatformEndpoints;

/// <summary>
/// Bootstrap recovery, the four directories, and the two kinds of Platform change (SPEC section 6).
/// <para>
/// There is no impersonation route, no deletion route, and no route that takes a tenant to act *as* — the acting
/// tenant always comes from the validated session. Those absences are asserted by tests rather than left to
/// review, because they are the whole difference between Platform and a back door (IA-REQ-046).
/// </para>
/// </summary>
internal static class PlatformEndpoints
{
    private static readonly ApiProblemContract[] Protected =
    [
        ApiProblemMetadata.AntiforgeryValidationFailed,
        ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        ApiProblemMetadata.RecentMfaRequired,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.InvalidPlatformOperation,
        ApiProblemMetadata.PlatformTenantConcurrencyConflict,
        ApiProblemMetadata.InternalServerError
    ];

    private static readonly ApiProblemContract[] Directory =
    [
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        // Reading an operational directory requires that this session proved the second factor, not only that it
        // holds the permission — so the refusal a password-only session meets is a declared answer (IA-REQ-045).
        ApiProblemMetadata.RecentMfaRequired,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.InternalServerError
    ];

    internal static void Map(RouteGroupBuilder group)
    {
        // Public, because it runs before the owner exists — and rate limited, because that is what stands in for
        // the authentication it cannot have (IA-REQ-040).
        group.MapPost("/bootstrap/recover", Recover)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.RateLimitExceeded,
                ApiProblemMetadata.InternalServerError);

        group.MapGet("/organizations", ListOrganizations)
            .RequireAuthorization()
            .Produces<PlatformOrganizationDirectoryResponse>()
            .WithApiProblemDetails(Directory);

        group.MapGet("/identities", ListIdentities)
            .RequireAuthorization()
            .Produces<PlatformIdentityDirectoryResponse>()
            .WithApiProblemDetails(Directory);

        group.MapGet("/admins", ListAdministrators)
            .RequireAuthorization()
            .Produces<PlatformAdministratorDirectoryResponse>()
            .WithApiProblemDetails(Directory);

        group.MapGet("/audit", ListAudit)
            .RequireAuthorization()
            .Produces<PlatformAuditDirectoryResponse>()
            .WithApiProblemDetails(Directory);

        group.MapPost("/organizations/{tenantId:guid}/suspend", Suspend)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Protected)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapPost("/organizations/{tenantId:guid}/reactivate", Reactivate)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Protected);

        group.MapPost("/admins/invitations", InviteAdministrator)
            .RequireAuthorization()
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(Protected)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapPost("/admins/{membershipId:guid}/revoke", RevokeAdministrator)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Protected);
    }

    /// <summary>
    /// Neutral by contract for every valid opaque state. Only two answers differ, and neither describes state: a
    /// missing antiforgery pair, and an exhausted limit with its <c>Retry-After</c>.
    /// </summary>
    private static async Task<IResult> Recover(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new RecoverPendingPlatformOwnerInvitationCommand(), context.RequestAborted);
        return result.IsSuccess
            ? Results.StatusCode(StatusCodes.Status202Accepted)
            : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> ListOrganizations(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? limit, string? cursor)
    {
        var result = await sender.Send(new ListPlatformOrganizationsQuery(Page(limit, cursor)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(new PlatformOrganizationDirectoryResponse(
            page.Items.Select(item => new PlatformOrganizationResponse(
                item.TenantId, item.Slug, item.Type, item.Status, item.CreatedAtUtc, item.UpdatedAtUtc,
                item.SuspensionReason, item.SuspendedAtUtc, item.AuthorizationVersion)).ToArray(),
            page.NextCursor)));
    }

    private static async Task<IResult> ListIdentities(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? limit, string? cursor)
    {
        var result = await sender.Send(new ListPlatformIdentitiesQuery(Page(limit, cursor)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(new PlatformIdentityDirectoryResponse(
            page.Items.Select(item => new PlatformIdentityResponse(
                item.IdentityId, item.NormalizedEmail, item.EmailConfirmed, item.IsLockedOut,
                item.MembershipCount, item.MfaStatus, item.LastSeenUtc)).ToArray(),
            page.NextCursor)));
    }

    private static async Task<IResult> ListAdministrators(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? limit, string? cursor)
    {
        var result = await sender.Send(new ListPlatformAdministratorsQuery(Page(limit, cursor)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(new PlatformAdministratorDirectoryResponse(
            page.Items.Select(item => new PlatformAdministratorResponse(
                item.MembershipId, item.IdentityId, item.NormalizedEmail, item.EmailConfirmed,
                item.MembershipStatus, item.MfaStatus, item.IsOwner, item.SinceUtc)).ToArray(),
            page.NextCursor)));
    }

    private static async Task<IResult> ListAudit(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? limit, string? cursor)
    {
        var result = await sender.Send(new ListPlatformAuditQuery(Page(limit, cursor)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(new PlatformAuditDirectoryResponse(
            page.Items.Select(item => new PlatformAuditEventResponse(
                item.EventId, item.EventType, item.OccurredAtUtc, item.CorrelationId,
                item.ActorIdentityId, item.TenantId, item.Outcome, item.ReasonCode)).ToArray(),
            page.NextCursor)));
    }

    private static async Task<IResult> Suspend(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid tenantId,
        PlatformTenantLifecycleRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        // An unknown name is a refusal the caller can act on, not a malformed body: the set is closed and
        // published, so naming something outside it is a decidable mistake.
        if (!Enum.TryParse<CleanArchitecture.Domain.IdentityAccess.Tenants.TenantSuspensionReason>(request.Reason, ignoreCase: false, out var reason) ||
            !Enum.IsDefined(reason))
        {
            return problems.ToHttpResult(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.InvalidPlatformOperation());
        }

        var result = await sender.Send(new SuspendOrganizationTenantCommand(tenantId, reason), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Reactivate(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid tenantId)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new ReactivateOrganizationTenantCommand(tenantId), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    /// <summary>Neutral by contract, for the same reason every other invitation is: it must not reveal the address.</summary>
    private static async Task<IResult> InviteAdministrator(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformAdministratorInvitationRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new InvitePlatformAdministratorCommand(request.Email), context.RequestAborted);
        return result.IsSuccess
            ? Results.StatusCode(StatusCodes.Status202Accepted)
            : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> RevokeAdministrator(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid membershipId)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new RevokePlatformAdministratorCommand(membershipId), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    /// <summary>A missing limit means the default page, not every row.</summary>
    private static PlatformDirectoryQuery Page(int? limit, string? cursor) => new(limit ?? 25, cursor);
}
