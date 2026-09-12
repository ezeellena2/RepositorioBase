using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess.Credentials;

public sealed class CredentialWriteResultTests
{
    [Test]
    public void Only_factories_can_construct_a_credential_write_outcome()
    {
        typeof(CredentialWriteResult).GetConstructors().ShouldBeEmpty();
        CredentialWriteResult.Applied().Succeeded.ShouldBeTrue();
        CredentialWriteResult.Applied().Failure.ShouldBe(CredentialWriteFailure.None);
        CredentialWriteResult.Concurrency().Succeeded.ShouldBeFalse();
        CredentialWriteResult.Concurrency().Errors.ShouldBeEmpty();
    }

    [Test]
    public void Password_policy_is_the_only_outcome_with_canonical_immutable_field_errors()
    {
        var result = CredentialWriteResult.PasswordPolicy();

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBe(CredentialWriteFailure.PasswordPolicy);
        result.Errors.Keys.ShouldBe(["newPassword"]);
        var detail = result.Errors["newPassword"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
        detail.Params.ShouldBeEmpty();

        result.Errors["newPassword"][0] = new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>());
        result.Errors["newPassword"].ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
    }

    [Test]
    public void Password_policy_preserves_safe_specific_details_and_defensively_copies_their_collection()
    {
        ValidationErrorDetail[] source =
        [
            new(ValidationErrorCodes.PasswordTooShort, new Dictionary<string, int> { ["min"] = 12 }),
            new(ValidationErrorCodes.PasswordRequiresDigit, new Dictionary<string, int>()),
        ];
        var result = CredentialWriteResult.PasswordPolicy(source);

        source[0] = new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>());
        result.Errors["newPassword"].Select(detail => detail.Code).ShouldBe([
            ValidationErrorCodes.PasswordTooShort,
            ValidationErrorCodes.PasswordRequiresDigit,
        ]);

        result.Errors["newPassword"][0] = new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>());
        result.Errors["newPassword"][0].Code.ShouldBe(ValidationErrorCodes.PasswordTooShort);
        result.Errors["newPassword"][0].Params["min"].ShouldBe(12);
    }

    [Test]
    public void Failure_outcomes_map_exhaustively_to_the_established_application_contract()
    {
        var policy = CredentialWriteResult.PasswordPolicy().ToApplicationError();
        var concurrency = CredentialWriteResult.Concurrency().ToApplicationError();

        policy.Code.ShouldBe("validation_failed");
        policy.Category.ShouldBe(ApplicationErrorCategory.Validation);
        policy.ValidationErrors["newPassword"].ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
        concurrency.Code.ShouldBe("identity_concurrency_conflict");
        concurrency.Category.ShouldBe(ApplicationErrorCategory.Conflict);
        concurrency.ValidationErrors.ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => CredentialWriteResult.Applied().ToApplicationError());
    }
}
