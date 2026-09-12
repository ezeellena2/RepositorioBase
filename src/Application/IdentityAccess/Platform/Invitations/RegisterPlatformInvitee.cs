using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Validation;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Invitations;

/// <summary>
/// The recipient of a Platform invitation answering it. They have no session — and may have no account at all —
/// so this request is public by declaration rather than by omission. It never creates or activates a Platform
/// membership: confirmation, sign-in and the MFA gates all still stand between it and any authority (IA-REQ-041).
/// </summary>
public sealed record RegisterPlatformInviteeCommand(string Token, string Password)
    : IRequest<Result>, IPublicRequest, ISensitiveRequest;

/// <summary>
/// Shape only. Whether the token resolves, and whether the address already has an account, are deliberately not
/// judged here: answering those differently from an unknown token is exactly the oracle the neutral flow exists
/// to prevent, so they belong to the handler, which answers all of them alike.
/// </summary>
public sealed class RegisterPlatformInviteeCommandValidator : AbstractValidator<RegisterPlatformInviteeCommand>
{
    public RegisterPlatformInviteeCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("An invitation token is required.")
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The invitation token must be 256 characters or fewer.")
            .OverridePropertyName("token");

        RuleFor(command => command.Password)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A password is required.")
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The password must be 256 characters or fewer.")
            .OverridePropertyName("password");
    }
}
