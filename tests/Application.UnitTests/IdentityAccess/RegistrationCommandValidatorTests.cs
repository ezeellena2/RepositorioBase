using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess;

public sealed class RegistrationCommandValidatorTests
{
    [Test]
    public void Anonymous_organization_registration_has_exact_lowercase_required_errors()
    {
        var result = OrganizationValidator(AnonymousSession).Validate(new RegisterOrganizationCommand(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty));

        AssertErrors(result,
            ("email", "Enter an email address."),
            ("password", "A password is required."),
            ("legalName", "A legal name is required."),
            ("cuit", "A CUIT is required."));
    }

    [Test]
    public void Organization_registration_has_exact_length_and_shape_errors_without_echoing_values()
    {
        var tooLong = OrganizationValidator(AnonymousSession).Validate(new RegisterOrganizationCommand(
            new string('e', 257),
            new string('p', 257),
            new string('n', 257),
            new string('1', 33)));

        AssertErrors(tooLong,
            ("email", "The email address must be 256 characters or fewer."),
            ("password", "The password must be 256 characters or fewer."),
            ("legalName", "The legal name must be 256 characters or fewer."),
            ("cuit", "The CUIT must be 32 characters or fewer."));

        const string invalidEmail = "organization-address";
        const string invalidCuit = "30/12345678/9";
        var invalidShape = OrganizationValidator(AnonymousSession).Validate(new RegisterOrganizationCommand(
            invalidEmail,
            "Valid-password-123!",
            "Example SA",
            invalidCuit));

        AssertErrors(invalidShape,
            ("email", "Enter an email address."),
            ("cuit", "The CUIT must contain exactly eleven digits and may use only digits, hyphens, and whitespace."));
        invalidShape.Errors.Select(error => error.ErrorMessage).ShouldAllBe(message =>
            !message.Contains(invalidEmail, StringComparison.Ordinal)
            && !message.Contains(invalidCuit, StringComparison.Ordinal));
    }

    [Test]
    public void Signed_in_organization_registration_allows_omitted_credentials_and_ignores_the_unused_password()
    {
        var validator = OrganizationValidator(new SessionStub(Guid.NewGuid(), "owner@example.test"));

        var omittedCredentials = validator.Validate(new RegisterOrganizationCommand(
            string.Empty,
            string.Empty,
            "Example SA",
            "30-12345678-1"));

        omittedCredentials.IsValid.ShouldBeTrue();

        var spoofedCredentials = validator.Validate(new RegisterOrganizationCommand(
            "somebody-else@example.test",
            new string('p', 257),
            "Example SA",
            "30-12345678-1"));

        spoofedCredentials.IsValid.ShouldBeTrue();
    }

    [Test]
    public void Invalid_and_email_only_sessions_do_not_run_anonymous_credential_validation()
    {
        var blankCredentials = new RegisterOrganizationCommand(
            string.Empty,
            string.Empty,
            "Example SA",
            "30-12345678-1");

        OrganizationValidator(new SessionStub(null, null, IsInvalid: true))
            .Validate(blankCredentials)
            .IsValid.ShouldBeTrue();
        OrganizationValidator(new SessionStub(null, "owner@example.test"))
            .Validate(blankCredentials)
            .IsValid.ShouldBeTrue();

        var malformedCredentials = blankCredentials with
        {
            Email = "not-an-email",
            Password = new string('p', 257)
        };
        OrganizationValidator(new SessionStub(null, "owner@example.test"))
            .Validate(malformedCredentials)
            .IsValid.ShouldBeTrue();
    }

    [Test]
    public void Organization_registration_rejects_a_wrong_check_digit_without_echoing_the_value()
    {
        const string submitted = "30-12345678-9";

        var result = OrganizationValidator(AnonymousSession).Validate(new RegisterOrganizationCommand(
            "owner@example.test",
            "Testing1234!",
            "Example SA",
            submitted));

        AssertErrors(result, ("cuit", "That CUIT's check digit does not match. Check the number."));
        result.Errors.ShouldAllBe(error =>
            !error.ErrorMessage.Contains(submitted, StringComparison.Ordinal)
            && !error.ErrorMessage.Contains("12345678", StringComparison.Ordinal));
    }

    [Test]
    public void Personal_registration_has_exact_lowercase_required_errors()
    {
        var result = PersonalValidator().Validate(new RegisterPersonalCommand(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty));

        AssertErrors(result,
            ("email", "Enter an email address."),
            ("password", "A password is required."),
            ("fullName", "A full name is required."),
            ("displayName", "A display name is required."),
            ("documentNumber", "A document number is required."));
    }

    [Test]
    public void Personal_registration_has_exact_length_and_shape_errors_without_echoing_values()
    {
        var tooLong = PersonalValidator().Validate(new RegisterPersonalCommand(
            new string('e', 257),
            new string('p', 257),
            new string('f', 201),
            new string('d', 61),
            new string('1', 33)));

        AssertErrors(tooLong,
            ("email", "The email address must be 256 characters or fewer."),
            ("password", "The password must be 256 characters or fewer."),
            ("fullName", "The full name must be 200 characters or fewer."),
            ("displayName", "The display name must be 60 characters or fewer."),
            ("documentNumber", "The document number must be 32 characters or fewer."));

        const string invalidEmail = "personal-address";
        const string invalidDocument = "12/345";
        var invalidShape = PersonalValidator().Validate(new RegisterPersonalCommand(
            invalidEmail,
            "Valid-password-123!",
            "Ada Lovelace",
            "Ada",
            invalidDocument));

        AssertErrors(invalidShape,
            ("email", "Enter an email address."),
            ("documentNumber", "An Argentine DNI must contain seven or eight digits and may use only digits, dots, hyphens, and whitespace."));
        invalidShape.Errors.Select(error => error.ErrorMessage).ShouldAllBe(message =>
            !message.Contains(invalidEmail, StringComparison.Ordinal)
            && !message.Contains(invalidDocument, StringComparison.Ordinal));
    }

    private static IValidator<RegisterOrganizationCommand> OrganizationValidator(IValidatedOptionalSession session)
    {
        var validatorType = typeof(RegisterOrganizationCommand).Assembly.GetType(
            "CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization.RegisterOrganizationCommandValidator");
        validatorType.ShouldNotBeNull("organization registration needs its own request-only validator");
        return (IValidator<RegisterOrganizationCommand>)Activator.CreateInstance(validatorType!, session)!;
    }

    private static IValidator<RegisterPersonalCommand> PersonalValidator()
    {
        var validatorType = typeof(RegisterPersonalCommand).Assembly.GetType(
            "CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommandValidator");
        validatorType.ShouldNotBeNull("personal registration needs its own request-only validator");
        return (IValidator<RegisterPersonalCommand>)Activator.CreateInstance(validatorType!)!;
    }

    private static void AssertErrors(ValidationResult result, params (string PropertyName, string Message)[] expected)
    {
        result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)).ShouldBe(expected);
    }

    private static readonly SessionStub AnonymousSession = new(null, null);

    private sealed record SessionStub(Guid? IdentityId, string? Email, bool IsInvalid = false) : IValidatedOptionalSession;
}
