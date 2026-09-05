using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.PlatformEndpoints.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.PlatformEndpoints;

/// <summary>
/// The MFA gates (SPEC section 6). Every one requires an authenticated, confirmed identity and antiforgery, and
/// none requires an active Platform tenant — the invitee has none until the last gate grants one.
/// </summary>
internal static class PlatformMfaEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/mfa/enroll", Enroll)
            .RequireAuthorization()
            .Produces<PlatformMfaEnrollmentResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(Gate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/verify", Verify)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(CodeGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/recovery-acknowledge", Acknowledge)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(Gate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/step-up", StepUp)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(CodeGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);
    }

    /// <summary>The same set for every gate, because every gate can fail in the same ways.</summary>
    private static readonly ApiProblemContract[] Gate =
    [
        ApiProblemMetadata.AntiforgeryValidationFailed,
        ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.InvalidInvitation,
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.InvitationConflict,
        ApiProblemMetadata.InternalServerError
    ];

    /// <summary>
    /// The two gates that accept an authenticator code answer everything the others do, plus the bounded-attempt
    /// refusal — the one answer a client must be able to tell apart from a wrong code (IA-REQ-041).
    /// </summary>
    private static readonly ApiProblemContract[] CodeGate = [.. Gate, ApiProblemMetadata.RateLimitExceeded];

    private static async Task<IResult> Enroll(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformMfaEnrollmentRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new BeginPlatformMfaEnrollmentCommand(request.Token), context.RequestAborted);
        return result.ToHttpResult(context, problems, details => Results.Ok(
            new PlatformMfaEnrollmentResponse(details.SharedKey, details.ProvisioningUri, details.RecoveryCodes)));
    }

    private static async Task<IResult> Verify(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformMfaVerificationRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new VerifyPlatformMfaEnrollmentCommand(request.Token, request.Code), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Acknowledge(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformMfaEnrollmentRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new AcknowledgePlatformRecoveryCodesCommand(request.Token), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> StepUp(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        PlatformMfaStepUpRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        var result = await sender.Send(new StepUpPlatformMfaCommand(request.Code), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }
}
