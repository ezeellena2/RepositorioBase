using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
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
        RuleFor(command => command.Token).NotEmpty().MaximumLength(256);
}

/// <summary>Proves the authenticator holds the secret. Until this succeeds, the factor is only half set up.</summary>
[Authorize(Permissions.PlatformMfaEnroll, false)]
public sealed record VerifyPlatformMfaEnrollmentCommand(string Token, string Code)
    : IRequest<Result>, ISensitiveRequest;

public sealed class VerifyPlatformMfaEnrollmentCommandValidator : AbstractValidator<VerifyPlatformMfaEnrollmentCommand>
{
    public VerifyPlatformMfaEnrollmentCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(16);
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
        RuleFor(command => command.Token).NotEmpty().MaximumLength(256);
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
        RuleFor(command => command.Code).NotEmpty().MaximumLength(16);
}
