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
                .NotEmpty().WithMessage("Enter an email address.")
                .MaximumLength(256).WithMessage("The email address must be 256 characters or fewer.")
                .Must(HasSupportedEmailShape).WithMessage("Enter an email address.")
                .OverridePropertyName("email");

            RuleFor(command => command.Password)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("A password is required.")
                .MaximumLength(256).WithMessage("The password must be 256 characters or fewer.")
                .OverridePropertyName("password");
        });

        RuleFor(command => command.LegalName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A legal name is required.")
            .MaximumLength(256).WithMessage("The legal name must be 256 characters or fewer.")
            .OverridePropertyName("legalName");

        RuleFor(command => command.Cuit)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A CUIT is required.")
            .MaximumLength(32).WithMessage("The CUIT must be 32 characters or fewer.")
            .OverridePropertyName("cuit")
            .Custom((cuit, context) =>
            {
                switch (NormalizedCuit.Evaluate(cuit, out _))
                {
                    case CuitRule.Characters or CuitRule.Length:
                        context.AddFailure("The CUIT must contain exactly eleven digits and may use only digits, hyphens, and whitespace.");
                        break;
                    case CuitRule.CheckDigit:
                        context.AddFailure("That CUIT's check digit does not match. Check the number.");
                        break;
                }
            });
    }

    private static bool HasSupportedEmailShape(string email) => email.Trim().Contains('@');
}
