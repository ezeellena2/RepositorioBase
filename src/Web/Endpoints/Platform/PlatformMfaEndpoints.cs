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
            .WithApiProblemDetails(InvitationGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/verify", Verify)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(VerificationGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/recovery-acknowledge", Acknowledge)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(InvitationGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/step-up", StepUp)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(StepUpGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidInvitation.Code);

        group.MapPost("/mfa/recover", Recover)
            .RequireAuthorization()
            .Produces<PlatformMfaEnrollmentResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(RecoveryGate)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidCredentialProof.Code);
    }

    private static readonly ApiProblemContract[] Protected =
    [
        ApiProblemMetadata.AntiforgeryValidationFailed,
        ApiProblemMetadata.ValidationFailed,
        ApiProblemMetadata.AuthenticationRequired,
        ApiProblemMetadata.InvalidSession,
        ApiProblemMetadata.PermissionDenied,
        ApiProblemMetadata.InternalServerError
    ];

    /// <summary>Enrollment and acknowledgement still operate on the invitation and can meet its conflict.</summary>
    private static readonly ApiProblemContract[] InvitationGate =
        [.. Protected, ApiProblemMetadata.InvalidInvitation, ApiProblemMetadata.InvitationConflict];

    /// <summary>Verification is invitation-bound, but a valid invitation never produces an invitation conflict.</summary>
    private static readonly ApiProblemContract[] VerificationGate =
        [.. Protected, ApiProblemMetadata.InvalidInvitation, ApiProblemMetadata.InvalidMfaCode, .. ApiProblemMetadata.BoundedAttempt];

    /// <summary>Step-up operates on the authenticated identity's factor, not on an invitation.</summary>
    private static readonly ApiProblemContract[] StepUpGate =
        [.. Protected, ApiProblemMetadata.InvalidMfaCode, .. ApiProblemMetadata.BoundedAttempt];

    /// <summary>Recovery operates on a recent proof and the identity's recovery material, not on an invitation.</summary>
    private static readonly ApiProblemContract[] RecoveryGate =
    [
        .. Protected,
        ApiProblemMetadata.RecentProofRequired,
        ApiProblemMetadata.InvalidRecoveryCode,
        ApiProblemMetadata.PlatformMfaConcurrencyConflict,
        .. ApiProblemMetadata.BoundedAttempt
    ];

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
        return result.ToHttpResult(context, problems);
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
        return result.ToHttpResult(context, problems);
    }

    private static async Task<IResult> Recover(
        HttpContext context,
        IAntiforgery antiforgery,
        ApiProblemDetailsMapper problems,
        ISender sender,
        RecoverPlatformMfaRequest request)
    {
        var failure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (failure is not null) return failure;

        // Shown exactly once, like the enrollment this replaces. There is no route that reads it back.
        var result = await sender.Send(new RecoverPlatformMfaCommand(request.RecoveryCode), context.RequestAborted);
        return result.ToHttpResult(context, problems, details => Results.Ok(
            new PlatformMfaEnrollmentResponse(details.SharedKey, details.ProvisioningUri, details.RecoveryCodes)));
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
        return result.ToHttpResult(context, problems);
    }
}
