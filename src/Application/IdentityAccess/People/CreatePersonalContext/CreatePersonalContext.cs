using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;

/// <summary>
/// Somebody who already has an account adding their own `Personal` context. It carries no email and no password:
/// the session is the proof, and asking for a credential again would be asking a person to prove what they just
/// proved (IA-REQ-048).
/// </summary>
[Authorize(Permissions.IdentityProfileManage, false)]
public sealed record CreatePersonalContextCommand(
    string FullName,
    string DisplayName,
    string DocumentNumber) : IRequest<Result>, ISensitiveRequest;

public sealed class CreatePersonalContextCommandValidator : AbstractValidator<CreatePersonalContextCommand>
{
    public CreatePersonalContextCommandValidator()
    {
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
