using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using FluentValidation;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess;

public sealed class InvitationRegistrationValidatorTests
{
    [Test]
    public void Organization_invitation_registration_has_explicit_lowercase_shape_errors()
    {
        var validatorType = typeof(RegisterInvitedUserCommand).Assembly.GetType(
            "CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser.RegisterInvitedUserCommandValidator");
        validatorType.ShouldNotBeNull("the public organization-invitation command needs its own request-only validator");

        var validator = (IValidator<RegisterInvitedUserCommand>)Activator.CreateInstance(validatorType!)!;
        AssertShapeErrors(validator, (token, password) => new RegisterInvitedUserCommand(token, password));
    }

    [Test]
    public void Platform_invitation_registration_uses_the_same_explicit_lowercase_shape_errors()
    {
        AssertShapeErrors(
            new RegisterPlatformInviteeCommandValidator(),
            (token, password) => new RegisterPlatformInviteeCommand(token, password));
    }

    private static void AssertShapeErrors<TCommand>(
        IValidator<TCommand> validator,
        Func<string, string, TCommand> command)
    {
        var missing = validator.Validate(command(string.Empty, string.Empty));

        missing.Errors.Select(error => (error.PropertyName, error.ErrorMessage)).ShouldBe([
            ("token", "An invitation token is required."),
            ("password", "A password is required.")
        ]);

        var overlong = validator.Validate(command(new string('t', 257), new string('p', 257)));

        overlong.Errors.Select(error => (error.PropertyName, error.ErrorMessage)).ShouldBe([
            ("token", "The invitation token must be 256 characters or fewer."),
            ("password", "The password must be 256 characters or fewer.")
        ]);
    }
}
