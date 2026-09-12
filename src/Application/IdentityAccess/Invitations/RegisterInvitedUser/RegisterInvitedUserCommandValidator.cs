using CleanArchitecture.Application.Common.Validation;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

public sealed class RegisterInvitedUserCommandValidator : AbstractValidator<RegisterInvitedUserCommand>
{
    public RegisterInvitedUserCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithMessage("An invitation token is required.")
            .WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256)
            .WithMessage("The invitation token must be 256 characters or fewer.")
            .WithErrorCode(ValidationErrorCodes.TooLong)
            .OverridePropertyName("token");

        RuleFor(command => command.Password)
            .NotEmpty()
            .WithMessage("A password is required.")
            .WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256)
            .WithMessage("The password must be 256 characters or fewer.")
            .WithErrorCode(ValidationErrorCodes.TooLong)
            .OverridePropertyName("password");
    }
}
