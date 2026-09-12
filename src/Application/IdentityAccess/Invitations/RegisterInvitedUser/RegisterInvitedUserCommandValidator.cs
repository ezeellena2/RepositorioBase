using CleanArchitecture.Application.Common.Validation;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

public sealed class RegisterInvitedUserCommandValidator : AbstractValidator<RegisterInvitedUserCommand>
{
    public RegisterInvitedUserCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("An invitation token is required.")
            .MaximumLength(256)
            .WithErrorCode(ValidationErrorCodes.TooLong)
            .WithMessage("The invitation token must be 256 characters or fewer.")
            .OverridePropertyName("token");

        RuleFor(command => command.Password)
            .NotEmpty()
            .WithErrorCode(ValidationErrorCodes.Required)
            .WithMessage("A password is required.")
            .MaximumLength(256)
            .WithErrorCode(ValidationErrorCodes.TooLong)
            .WithMessage("The password must be 256 characters or fewer.")
            .OverridePropertyName("password");
    }
}
