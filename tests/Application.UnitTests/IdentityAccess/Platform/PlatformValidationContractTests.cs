using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using FluentValidation;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess.Platform;

public sealed class PlatformValidationContractTests
{
    [Test]
    public void Mfa_validators_use_wire_names_and_explicit_safe_messages()
    {
        AssertTokenErrors(
            new BeginPlatformMfaEnrollmentCommandValidator(),
            token => new BeginPlatformMfaEnrollmentCommand(token));
        AssertTokenErrors(
            new AcknowledgePlatformRecoveryCodesCommandValidator(),
            token => new AcknowledgePlatformRecoveryCodesCommand(token));

        AssertErrors(
            new VerifyPlatformMfaEnrollmentCommandValidator(),
            new VerifyPlatformMfaEnrollmentCommand(string.Empty, string.Empty),
            [
                ("token", "An invitation token is required."),
                ("code", "An authenticator code is required.")
            ]);
        AssertErrors(
            new VerifyPlatformMfaEnrollmentCommandValidator(),
            new VerifyPlatformMfaEnrollmentCommand(new string('t', 257), new string('1', 17)),
            [
                ("token", "The invitation token must be 256 characters or fewer."),
                ("code", "The authenticator code must be 16 characters or fewer.")
            ]);

        AssertErrors(
            new RecoverPlatformMfaCommandValidator(),
            new RecoverPlatformMfaCommand(string.Empty),
            [("recoveryCode", "A recovery code is required.")]);
        AssertErrors(
            new RecoverPlatformMfaCommandValidator(),
            new RecoverPlatformMfaCommand(new string('r', 65)),
            [("recoveryCode", "The recovery code must be 64 characters or fewer.")]);

        AssertErrors(
            new StepUpPlatformMfaCommandValidator(),
            new StepUpPlatformMfaCommand(string.Empty),
            [("code", "An authenticator code is required.")]);
        AssertErrors(
            new StepUpPlatformMfaCommandValidator(),
            new StepUpPlatformMfaCommand(new string('1', 17)),
            [("code", "The authenticator code must be 16 characters or fewer.")]);
    }

    [Test]
    public void Platform_invitation_confirmation_uses_wire_name_and_explicit_safe_messages()
    {
        var validator = new ConfirmPlatformInviteeCommandValidator();

        AssertErrors(
            validator,
            new ConfirmPlatformInviteeCommand(string.Empty),
            [("confirmationToken", "A confirmation token is required.")]);
        AssertErrors(
            validator,
            new ConfirmPlatformInviteeCommand(new string('t', 257)),
            [("confirmationToken", "The confirmation token must be 256 characters or fewer.")]);
    }

    [Test]
    public void Platform_administrator_invitation_uses_wire_name_and_explicit_safe_messages()
    {
        var validator = new InvitePlatformAdministratorCommandValidator();

        AssertErrors(
            validator,
            new InvitePlatformAdministratorCommand(string.Empty),
            [("email", "Enter an email address.")]);
        AssertErrors(
            validator,
            new InvitePlatformAdministratorCommand(new string('a', 257)),
            [("email", "The email address must be 256 characters or fewer.")]);
    }

    private static void AssertTokenErrors<TCommand>(IValidator<TCommand> validator, Func<string, TCommand> command)
    {
        AssertErrors(
            validator,
            command(string.Empty),
            [("token", "An invitation token is required.")]);
        AssertErrors(
            validator,
            command(new string('t', 257)),
            [("token", "The invitation token must be 256 characters or fewer.")]);
    }

    private static void AssertErrors<TCommand>(
        IValidator<TCommand> validator,
        TCommand command,
        (string PropertyName, string ErrorMessage)[] expected)
    {
        validator.Validate(command).Errors
            .Select(error => (error.PropertyName, error.ErrorMessage))
            .ShouldBe(expected);
    }
}
