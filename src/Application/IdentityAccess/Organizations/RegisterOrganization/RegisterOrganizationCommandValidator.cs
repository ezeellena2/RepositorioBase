using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using FluentValidation.Results;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public RegisterOrganizationCommandValidator(IValidatedOptionalSession session)
    {
        When(_ => !session.IsInvalid && session.IdentityId is null && session.Email is null, () =>
        {
            RuleFor(command => command.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Enter an email address.").WithErrorCode(ValidationErrorCodes.Required)
                .MaximumLength(256).WithMessage("The email address must be 256 characters or fewer.").WithErrorCode(ValidationErrorCodes.TooLong)
                .Must(HasSupportedEmailShape).WithMessage("Enter an email address.").WithErrorCode(ValidationErrorCodes.Invalid)
                .OverridePropertyName("email");

            RuleFor(command => command.Password)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("A password is required.").WithErrorCode(ValidationErrorCodes.Required)
                .MaximumLength(256).WithMessage("The password must be 256 characters or fewer.").WithErrorCode(ValidationErrorCodes.TooLong)
                .OverridePropertyName("password");
        });

        RuleFor(command => command.LegalName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A legal name is required.").WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(256).WithMessage("The legal name must be 256 characters or fewer.").WithErrorCode(ValidationErrorCodes.TooLong)
            .OverridePropertyName("legalName");

        RuleFor(command => command.Cuit)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A CUIT is required.").WithErrorCode(ValidationErrorCodes.Required)
            .MaximumLength(32).WithMessage("The CUIT must be 32 characters or fewer.").WithErrorCode(ValidationErrorCodes.TooLong)
            .OverridePropertyName("cuit")
            .Custom((cuit, context) =>
            {
                switch (NormalizedCuit.Evaluate(cuit, out _))
                {
                    case CuitRule.Characters or CuitRule.Length:
                        context.AddFailure(new ValidationFailure(
                            context.PropertyPath,
                            "The CUIT must contain exactly eleven digits and may use only digits, hyphens, and whitespace.")
                        {
                            ErrorCode = ValidationErrorCodes.Invalid,
                        });
                        break;
                    case CuitRule.CheckDigit:
                        context.AddFailure(new ValidationFailure(
                            context.PropertyPath,
                            "That CUIT's check digit does not match. Check the number.")
                        {
                            ErrorCode = ValidationErrorCodes.Invalid,
                        });
                        break;
                }
            })
            .WithErrorCode(ValidationErrorCodes.Invalid);
    }

    private static bool HasSupportedEmailShape(string email) => email.Trim().Contains('@');
}
