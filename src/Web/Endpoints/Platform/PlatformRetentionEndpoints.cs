using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.PlatformEndpoints;

/// <summary>
/// Reading the retention policy, and placing or releasing a legal hold (IA-REQ-056, C7).
/// <para>
/// There is no purge route and there never will be: erasure is the maintenance worker's, driven by policy, with
/// no endpoint and no permission. What an operator can do here is see what the policy says and stop a deletion —
/// never order one.
/// </para>
/// </summary>
internal static class PlatformRetentionEndpoints
{
    private static readonly ApiProblemContract[] Read =
    [
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        ApiProblemMetadata.RecentMfaRequired,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.InternalServerError
    ];

    private static readonly ApiProblemContract[] Manage =
    [
        ApiProblemMetadata.AntiforgeryValidationFailed,
        ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        ApiProblemMetadata.RecentMfaRequired,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.InvalidPlatformOperation,
        ApiProblemMetadata.NotFound,
        ApiProblemMetadata.RetentionHoldConflict,
        ApiProblemMetadata.RetentionHoldSubjectPurged,
        ApiProblemMetadata.InternalServerError
    ];

    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/retention/policy", ReadPolicy)
            .RequireAuthorization()
            .Produces<RetentionPolicyView>(StatusCodes.Status200OK)
            .WithApiProblemDetails(Read);

        group.MapPost("/retention/holds", PlaceHold)
            .RequireAuthorization()
            .Produces<RetentionHoldView>(StatusCodes.Status201Created)
            .WithApiProblemDetails(Manage)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidPlatformOperation.Code);

        group.MapDelete("/retention/holds/{holdId:guid}", ReleaseHold)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Manage);
    }

    private static async Task<IResult> ReadPolicy(HttpContext context, ApiProblemDetailsMapper problems, ISender sender)
    {
        var result = await sender.Send(new GetRetentionPolicyQuery(), context.RequestAborted);
        return result.IsSuccess ? Results.Ok(result.Value) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> PlaceHold(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformRetentionHoldRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(
            new PlaceRetentionHoldCommand(request.SubjectIdentityId, request.ReasonCode ?? string.Empty, request.Reference ?? string.Empty),
            context.RequestAborted);
        return result.IsSuccess
            ? Results.Created($"/api/platform/retention/holds/{result.Value!.HoldId}", result.Value)
            : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> ReleaseHold(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        Guid holdId)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        // Bodyless and idempotent: an already-released hold and one that never existed answer alike, because
        // "does this hold exist" is not a question this route is for.
        var result = await sender.Send(new ReleaseRetentionHoldCommand(holdId), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }
}

/// <summary>
/// Placing one hold. Both fields are short stable identifiers an operator already has — a reason code from their
/// own set and their own case number — never prose about the person the hold names.
/// </summary>
public sealed record PlatformRetentionHoldRequest(Guid SubjectIdentityId, string? ReasonCode, string? Reference);
