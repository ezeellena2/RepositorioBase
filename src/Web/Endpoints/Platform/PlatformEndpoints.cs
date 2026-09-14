using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Application.IdentityAccess.Platform.Identities;
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
                ApiProblemMetadata.ServiceUnavailable,
                ApiProblemMetadata.InternalServerError);

        group.MapGet("/organizations", ListOrganizations)
            .RequireAuthorization()
            .Produces<PlatformOrganizationDirectoryResponse>()
            .WithApiProblemDetails(Directory)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapGet("/identities", ListIdentities)
            .RequireAuthorization()
            .Produces<PlatformIdentityDirectoryResponse>()
            .WithApiProblemDetails(Directory)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapGet("/admins", ListAdministrators)
            .RequireAuthorization()
            .Produces<PlatformAdministratorDirectoryResponse>()
            .WithApiProblemDetails(Directory)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapGet("/audit", ListAudit)
            .RequireAuthorization()
            .Produces<PlatformAuditDirectoryResponse>()
            .WithApiProblemDetails(Directory)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapPost("/organizations/{tenantId:guid}/suspend", Suspend)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Protected)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapPost("/organizations/{tenantId:guid}/reactivate", Reactivate)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Protected);

        group.MapPost("/identities/{identityId:guid}/suspend", SuspendIdentity)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails([.. Protected, ApiProblemMetadata.NotFound, ApiProblemMetadata.PlatformLastOwner, ApiProblemMetadata.IdentityConcurrencyConflict])
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapPost("/identities/{identityId:guid}/reactivate", ReactivateIdentity)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails([.. Protected, ApiProblemMetadata.NotFound, ApiProblemMetadata.IdentityReactivationUnavailable, ApiProblemMetadata.IdentityConcurrencyConflict])
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapPost("/identities/{identityId:guid}/document-disputes/{disputeId:guid}/resolve", ResolveDocumentDispute)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails([
                .. Protected,
                ApiProblemMetadata.NotFound,
                ApiProblemMetadata.InvalidDocumentDispute,
                ApiProblemMetadata.SelfResolutionRefused,
                ApiProblemMetadata.DocumentAlreadyRecorded,
                ApiProblemMetadata.PersonalProfileNotFound])
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidDocumentDispute.Code);

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
        return result.ToAcceptedHttpResult(context, problems);
    }

    private static async Task<IResult> ListOrganizations(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListPlatformOrganizationsQuery(PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(PlatformOrganizationDirectoryResponse.From(page, item =>
            new PlatformOrganizationResponse(
                item.TenantId, item.Slug, item.Type, item.Status, item.CreatedAtUtc, item.UpdatedAtUtc,
                item.SuspensionReason, item.SuspendedAtUtc, item.AuthorizationVersion))));
    }

    private static async Task<IResult> ListIdentities(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListPlatformIdentitiesQuery(PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(PlatformIdentityDirectoryResponse.From(page, item =>
            new PlatformIdentityResponse(
                item.IdentityId, item.NormalizedEmail, item.AccountStatus, item.EmailConfirmed, item.IsLockedOut,
                item.MembershipCount, item.MfaStatus, item.LastSeenUtc))));
    }

    private static async Task<IResult> ListAdministrators(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListPlatformAdministratorsQuery(PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(PlatformAdministratorDirectoryResponse.From(page, item =>
            new PlatformAdministratorResponse(
                item.MembershipId, item.IdentityId, item.NormalizedEmail, item.EmailConfirmed,
                item.MembershipStatus, item.MfaStatus, item.IsOwner, item.SinceUtc))));
    }

    private static async Task<IResult> ListAudit(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListPlatformAuditQuery(PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(PlatformAuditDirectoryResponse.From(page, item =>
            new PlatformAuditEventResponse(
                item.EventId, item.EventType, item.OccurredAtUtc, item.CorrelationId,
                item.ActorIdentityId, item.TenantId, item.Outcome, item.ReasonCode))));
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
        return result.ToHttpResult(context, problems);
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
        return result.ToHttpResult(context, problems);
    }

    private static async Task<IResult> SuspendIdentity(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid identityId,
        SuspendIdentityRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        // Both sets are closed and published, so naming something outside either is a decidable mistake rather
        // than a malformed body.
        if (!Enum.TryParse<CleanArchitecture.Domain.IdentityAccess.Identities.IdentitySuspensionReason>(request.Reason, ignoreCase: false, out var reason) ||
            !Enum.IsDefined(reason) ||
            !TryReadStatus(request.ExpectedStatus, out var expected))
        {
            return problems.ToHttpResult(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.InvalidPlatformOperation());
        }

        var result = await sender.Send(new SuspendIdentityCommand(identityId, reason, expected), context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static async Task<IResult> ReactivateIdentity(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid identityId,
        ReactivateIdentityRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;
        if (!TryReadStatus(request.ExpectedStatus, out var expected))
        {
            return problems.ToHttpResult(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.InvalidPlatformOperation());
        }

        var result = await sender.Send(
            new ReactivateIdentityCommand(identityId, expected, request.AcknowledgeSelfDeactivation), context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    /// <summary>
    /// The operator's half of IA-REQ-058. It names a stored dispute, and there is no route in this system that
    /// writes a document value without one.
    /// </summary>
    private static async Task<IResult> ResolveDocumentDispute(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid identityId,
        Guid disputeId,
        ResolveDocumentDisputeRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(
            new CleanArchitecture.Application.IdentityAccess.People.Documents.ResolveDocumentDisputeCommand(
                identityId, disputeId, request.Outcome ?? string.Empty, request.EvidenceReference ?? string.Empty),
            context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static bool TryReadStatus(string? value, out CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus status) =>
        Enum.TryParse(value, ignoreCase: false, out status) && Enum.IsDefined(status);

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
        return result.ToAcceptedHttpResult(context, problems);
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
        return result.ToHttpResult(context, problems);
    }
}
