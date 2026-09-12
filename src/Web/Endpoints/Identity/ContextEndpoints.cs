using CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Application.IdentityAccess.Context.SetPreferredLanguage;
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
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);
        group.MapPut("/context/tenant", SelectTenant)
            .RequireAuthorization()
            .Produces<IdentityContextResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.SessionConcurrencyConflict, ApiProblemMetadata.InternalServerError);
        group.MapPut("/context/language", SetPreferredLanguage)
            .RequireAuthorization()
            .Produces<IdentityContextResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.ValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.ValidationFailed.Code);
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

    private static async Task<IResult> SetPreferredLanguage(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        SetPreferredLanguageRequest request)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        // Resolve the response before mutating the account. Returning an error after a durable preference write
        // would make a refused request indistinguishable from an accepted one to the browser.
        var current = await sender.Send(new GetIdentityContextQuery(), context.RequestAborted);
        if (!current.IsSuccess) return problems.ToHttpResult(current.Error!);

        var update = await sender.Send(new SetPreferredLanguageCommand(request.Language), context.RequestAborted);
        if (!update.IsSuccess) return problems.ToHttpResult(update.Error!);

        return Results.Ok(IdentityContextResponse.From(current.Value! with { PreferredLanguage = request.Language }));
    }

    private sealed record SetPreferredLanguageRequest(string Language);
}
