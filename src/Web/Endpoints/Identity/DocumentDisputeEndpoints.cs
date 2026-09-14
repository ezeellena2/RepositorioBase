using CleanArchitecture.Application.IdentityAccess.People.Documents;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// The owner's half of correcting a recorded document (IA-REQ-058).
/// <para>
/// There is no route here that writes a document value, and that is the point. A person can say what they think
/// theirs should be; turning that into a change takes a second party with a case reference held outside this
/// system.
/// </para>
/// </summary>
internal static class DocumentDisputeEndpoints
{
    internal static void Map(RouteGroupBuilder group) =>
        group.MapPost("/profile/document/disputes", Open)
            .RequireAuthorization()
            .Produces<OpenedDocumentDispute>(StatusCodes.Status201Created)
            .WithApiProblemDetails(
                ApiProblemMetadata.AntiforgeryValidationFailed,
                ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.InvalidSession,
                ApiProblemMetadata.PermissionDenied,
                ApiProblemMetadata.RecentProofRequired,
                ApiProblemMetadata.InvalidDocumentDispute,
                ApiProblemMetadata.DocumentDisputeConflict,
                ApiProblemMetadata.PersonalProfileNotFound,
                ApiProblemMetadata.InternalServerError)
            // A body this route cannot read is answered without saying what was wrong with it: the one field in
            // scope is a document number.
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidDocumentDispute.Code);

    private static async Task<IResult> Open(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        OpenDocumentDisputeRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(
            new OpenDocumentDisputeCommand(
                request.ClaimedCountry ?? string.Empty,
                request.ClaimedType ?? string.Empty,
                request.ClaimedNumber ?? string.Empty,
                request.ReasonCode ?? string.Empty),
            context.RequestAborted);

        // The answer carries an opaque identifier and nothing else. Echoing any part of the claim back would
        // undo the protection applied a moment earlier.
        return result.ToHttpResult(context, problems, dispute =>
            Results.Created($"/api/identity/profile/document/disputes/{dispute.DisputeId}", dispute));
    }
}

/// <summary>
/// What a person claims their document should be. `claimedNumber` is protected on arrival exactly like the
/// recorded one and is never echoed, logged, audited, returned or written into an outbox payload.
/// </summary>
public sealed record OpenDocumentDisputeRequest(string? ClaimedCountry, string? ClaimedType, string? ClaimedNumber, string? ReasonCode);
