using CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.IdentityEndpoints.Contracts;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.AspNetCore.Antiforgery;
using CleanArchitecture.Web.Infrastructure;

namespace CleanArchitecture.Web.IdentityEndpoints;

internal static class ContextEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/context", Get)
            .RequireAuthorization()
            .Produces<IdentityContextResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.InternalServerError);
        group.MapPut("/context/tenant", SelectTenant)
            .RequireAuthorization()
            .Produces<IdentityContextResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.SessionConcurrencyConflict, ApiProblemMetadata.InternalServerError);
    }

    private static async Task<IResult> Get(ISender sender, ApiProblemDetailsMapper problems, HttpContext context)
    {
        var result = await sender.Send(new GetIdentityContextQuery(), context.RequestAborted);
        return result.IsSuccess ? Results.Ok(IdentityContextResponse.From(result.Value!)) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> SelectTenant(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, SelectTenantRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (request.TenantId == Guid.Empty)
        {
            return problems.ToHttpResult(new CleanArchitecture.Application.Common.Models.ApplicationError("invalid_request", CleanArchitecture.Application.Common.Models.ApplicationErrorCategory.Validation));
        }
        var result = await sender.Send(new SelectTenantCommand(TenantId.From(request.TenantId)), context.RequestAborted);
        return result.IsSuccess ? Results.Ok(IdentityContextResponse.From(result.Value!)) : problems.ToHttpResult(result.Error!);
    }

    private sealed record SelectTenantRequest(Guid TenantId);
}
