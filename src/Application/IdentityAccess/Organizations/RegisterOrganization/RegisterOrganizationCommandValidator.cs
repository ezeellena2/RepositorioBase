using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Organizations;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public RegisterOrganizationCommandValidator(IValidatedOptionalSession session)
    {
        When(_ => !session.IsInvalid && session.IdentityId is null && session.Email is null, () =>
        {
            RuleFor(command => command.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("Enter an email address.")
                .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The email address must be 256 characters or fewer.")
                .Must(HasSupportedEmailShape).WithErrorCode(ValidationErrorCodes.Invalid).WithMessage("Enter an email address.")
                .OverridePropertyName("email");

            RuleFor(command => command.Password)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A password is required.")
                .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The password must be 256 characters or fewer.")
                .OverridePropertyName("password");
        });

        RuleFor(command => command.LegalName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A legal name is required.")
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The legal name must be 256 characters or fewer.")
            .OverridePropertyName("legalName");

        RuleFor(command => command.Cuit)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A CUIT is required.")
            .MaximumLength(32).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The CUIT must be 32 characters or fewer.")
            .Must(cuit => NormalizedCuit.Evaluate(cuit, out _) is not (CuitRule.Characters or CuitRule.Length))
            .WithErrorCode(ValidationErrorCodes.Invalid)
            .WithMessage("The CUIT must contain exactly eleven digits and may use only digits, hyphens, and whitespace.")
            .Must(cuit => NormalizedCuit.Evaluate(cuit, out _) is not CuitRule.CheckDigit)
            .WithErrorCode(ValidationErrorCodes.Invalid)
            .WithMessage("That CUIT's check digit does not match. Check the number.")
            .OverridePropertyName("cuit");
    }

    private static bool HasSupportedEmailShape(string email) => email.Trim().Contains('@');
}
