using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// Custom roles inside one `Organization` (IA-REQ-053).
/// <para>
/// Every route is addressed by tenant and every handler compares that address against the session's active
/// tenant rather than trusting it. The three writes each spend a recent identity proof (amendment D2): without
/// one, `roles.manage` would be an unproved super-permission whose holder can package everything they hold into
/// a role and hand it to anybody.
/// </para>
/// </summary>
internal static class RoleEndpoints
{
    internal static void MapTenantScoped(RouteGroupBuilder group)
    {
        group.MapGet("/{tenantId:guid}/permission-catalog", Catalog)
            .RequireAuthorization()
            .Produces<IReadOnlyList<PermissionCatalogEntryResponse>>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);

        group.MapGet("/{tenantId:guid}/roles", List)
            .RequireAuthorization()
            .Produces<RolePageResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);

        group.MapGet("/{tenantId:guid}/roles/{roleId:guid}", Get)
            .RequireAuthorization()
            .Produces<RoleResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);

        group.MapPost("/{tenantId:guid}/roles", Create)
            .RequireAuthorization()
            .WithCreatedLocation<RoleResponse>()
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.InvalidRoleOperation, ApiProblemMetadata.RoleConcurrencyConflict, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);

        group.MapPut("/{tenantId:guid}/roles/{roleId:guid}", Update)
            .RequireAuthorization()
            .Produces<RoleResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.InvalidRoleOperation, ApiProblemMetadata.NotFound, ApiProblemMetadata.RoleConcurrencyConflict, ApiProblemMetadata.LastAdministratorRequired, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);

        group.MapPost("/{tenantId:guid}/roles/{roleId:guid}/retire", Retire)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.InvalidRoleOperation, ApiProblemMetadata.NotFound, ApiProblemMetadata.RoleConcurrencyConflict, ApiProblemMetadata.LastAdministratorRequired, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRoleOperation.Code);
    }

    private static async Task<IResult> Catalog(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId)
    {
        var result = await sender.Send(new GetPermissionCatalogQuery(TenantId.From(tenantId)), context.RequestAborted);
        return result.ToHttpResult(context, problems, catalog =>
            Results.Ok(catalog.Select(entry => new PermissionCatalogEntryResponse(entry.Code, entry.Grantable)).ToArray()));
    }

    private static async Task<IResult> List(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, int? pageNumber, int? pageSize)
    {
        var result = await sender.Send(new ListRolesQuery(TenantId.From(tenantId), PaginationQuery.From(pageNumber, pageSize)), context.RequestAborted);
        return result.ToHttpResult(context, problems, page => Results.Ok(RolePageResponse.From(page, Describe)));
    }

    private static async Task<IResult> Get(HttpContext context, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid roleId)
    {
        var result = await sender.Send(new GetRoleQuery(TenantId.From(tenantId), roleId), context.RequestAborted);
        return result.ToHttpResult(context, problems, role => Results.Ok(Describe(role)));
    }

    private static async Task<IResult> Create(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, CreateRoleRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new CreateRoleCommand(TenantId.From(tenantId), body.Name, body.Permissions), context.RequestAborted);
        return result.ToHttpResult(context, problems, role =>
            Results.Created($"/api/tenants/{tenantId}/roles/{role.RoleId}", Describe(role)));
    }

    private static async Task<IResult> Update(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid roleId, UpdateRoleRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new UpdateRoleCommand(TenantId.From(tenantId), roleId, body.Name, body.Permissions, body.Version), context.RequestAborted);
        return result.ToHttpResult(context, problems, role => Results.Ok(Describe(role)));
    }

    private static async Task<IResult> Retire(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, Guid tenantId, Guid roleId, RetireRoleRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var result = await sender.Send(new RetireRoleCommand(TenantId.From(tenantId), roleId, body.Version), context.RequestAborted);
        return result.ToHttpResult(context, problems);
    }

    private static RoleResponse Describe(RoleView role) =>
        new(role.RoleId, role.Name, role.IsSystem, role.IsRetired, role.Permissions, role.Version);
}

public sealed record PermissionCatalogEntryResponse(string Code, bool Grantable);

public sealed record RoleResponse(Guid RoleId, string Name, bool IsSystem, bool IsRetired, IReadOnlyList<string> Permissions, string Version);

public sealed record RolePageResponse(
    IReadOnlyList<RoleResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static RolePageResponse From(PaginatedList<RoleView> page, Func<RoleView, RoleResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record CreateRoleRequest(string? Name, IReadOnlyList<string>? Permissions);

public sealed record UpdateRoleRequest(string? Name, IReadOnlyList<string>? Permissions, string? Version);

public sealed record RetireRoleRequest(string? Version);
