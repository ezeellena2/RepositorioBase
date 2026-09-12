using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;

public sealed class RegisterPersonalCommandValidator : AbstractValidator<RegisterPersonalCommand>
{
    public RegisterPersonalCommandValidator()
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

        RuleFor(command => command.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A full name is required.")
            .MaximumLength(200).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The full name must be 200 characters or fewer.")
            .OverridePropertyName("fullName");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A display name is required.")
            .MaximumLength(60).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The display name must be 60 characters or fewer.")
            .OverridePropertyName("displayName");

        RuleFor(command => command.DocumentNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A document number is required.")
            .MaximumLength(32).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The document number must be 32 characters or fewer.")
            .Must(HasSupportedDocumentShape)
            .WithErrorCode(ValidationErrorCodes.Invalid)
            .WithMessage("An Argentine DNI must contain seven or eight digits and may use only digits, dots, hyphens, and whitespace.")
            .OverridePropertyName("documentNumber");
    }

    private static bool HasSupportedEmailShape(string email) => email.Trim().Contains('@');

    private static bool HasSupportedDocumentShape(string documentNumber)
    {
        try
        {
            _ = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, documentNumber);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
