using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

/// <summary>
/// What an enrollment hands its owner, exactly once: the key to put into an authenticator, and the one-time
/// recovery codes. Neither is ever readable again — the secret is stored encrypted and the codes only as hashes.
/// </summary>
public sealed record PlatformMfaEnrollmentDetails(string SharedKey, string ProvisioningUri, IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Begins — or restarts — the second factor for the invitee this session belongs to (IA-REQ-041).
/// <para>
/// It is authorized rather than public: the caller must be signed in and confirmed, and the invitation token only
/// says which offer is being answered. A token-only request would let anyone holding the link enroll on the
/// recipient's behalf. The permission is application-scoped because the invitee holds no membership yet — there
/// is no tenant to scope it to until the gates complete.
/// </para>
/// </summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record BeginPlatformMfaEnrollmentCommand(string Token)
    : IRequest<Result<PlatformMfaEnrollmentDetails>>, ISensitiveRequest;

public sealed class BeginPlatformMfaEnrollmentCommandValidator : AbstractValidator<BeginPlatformMfaEnrollmentCommand>
{
    public BeginPlatformMfaEnrollmentCommandValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong);
}

/// <summary>Proves the authenticator holds the secret. Until this succeeds, the factor is only half set up.</summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record VerifyPlatformMfaEnrollmentCommand(string Token, string Code)
    : IRequest<Result>, ISensitiveRequest;

public sealed class VerifyPlatformMfaEnrollmentCommandValidator : AbstractValidator<VerifyPlatformMfaEnrollmentCommand>
{
    public VerifyPlatformMfaEnrollmentCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong);
        RuleFor(command => command.Code)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(16).WithErrorCode(ValidationErrorCodes.TooLong);
    }
}

/// <summary>
/// The last gate. Acknowledging the recovery codes is what activates the Platform membership the invitation was
/// for — and it is the only thing that ever does.
/// </summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record AcknowledgePlatformRecoveryCodesCommand(string Token)
    : IRequest<Result>, ISensitiveRequest;

public sealed class AcknowledgePlatformRecoveryCodesCommandValidator : AbstractValidator<AcknowledgePlatformRecoveryCodesCommand>
{
    public AcknowledgePlatformRecoveryCodesCommandValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong);
}

/// <summary>
/// Replaces a second factor whose authenticator is gone, paid for with one unspent recovery code (IA-REQ-041, C6).
/// <para>
/// It is the only route that replaces a working factor without proving that factor, which is why everything else
/// about the caller has to be true at once: a session belonging to a confirmed identity, `platform.mfa.enroll`,
/// antiforgery, a password proved a moment ago for this action alone, and a code nobody has spent. Holding the
/// code is not enough, and neither is holding the password.
/// </para>
/// <para>
/// SPEC's route table writes the proof as a `proofToken` field. It is not one here, for the same reason no other
/// sensitive route has one: C4's proofs are server-side rows spent by identity, session and action, and nothing
/// the client holds names one. The gate is the same gate; only its spelling differs.
/// </para>
/// <para>
/// The same table says the route needs "no active Platform tenant". That is an absence from the requirement list,
/// not a prohibition: every other Platform change needs one and a step-up on top, and this route cannot, because
/// the person reaching it is the one who cannot step up. Reading it as a prohibition would make the route
/// unreachable for exactly that person — signing in selects the only tenant an operator belongs to — so the
/// permission stays application-scoped and no tenant is checked either way. Nothing is lost by that: the bar is
/// still an unspent recovery code and a password proved a moment ago.
/// </para>
/// </summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record RecoverPlatformMfaCommand(string RecoveryCode)
    : IRequest<Result<PlatformMfaEnrollmentDetails>>, ISensitiveRequest;

public sealed class RecoverPlatformMfaCommandValidator : AbstractValidator<RecoverPlatformMfaCommand>
{
    public RecoverPlatformMfaCommandValidator() =>
        RuleFor(command => command.RecoveryCode)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(64).WithErrorCode(ValidationErrorCodes.TooLong);
}

/// <summary>
/// Re-proves the factor for an administrator who already holds Platform authority, which is what a Platform
/// mutation requires to have happened recently (IA-REQ-041/043). It needs no invitation token: the invitation was
/// consumed at activation, and the caller's authority now comes from their active Platform membership.
/// </summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record StepUpPlatformMfaCommand(string Code) : IRequest<Result>, ISensitiveRequest;

public sealed class StepUpPlatformMfaCommandValidator : AbstractValidator<StepUpPlatformMfaCommand>
{
    public StepUpPlatformMfaCommandValidator() =>
        RuleFor(command => command.Code)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(16).WithErrorCode(ValidationErrorCodes.TooLong);
}
