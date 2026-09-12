using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

public sealed class RegisterInvitedUserCommandValidator : AbstractValidator<RegisterInvitedUserCommand>
{
    public RegisterInvitedUserCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithMessage("An invitation token is required.")
            .MaximumLength(256)
            .WithMessage("The invitation token must be 256 characters or fewer.")
            .OverridePropertyName("token");

        RuleFor(command => command.Password)
            .NotEmpty()
            .WithMessage("A password is required.")
            .MaximumLength(256)
            .WithMessage("The password must be 256 characters or fewer.")
            .OverridePropertyName("password");
    }
}
