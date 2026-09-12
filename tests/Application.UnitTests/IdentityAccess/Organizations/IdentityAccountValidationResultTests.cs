using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess.Organizations;

public sealed class IdentityAccountValidationResultTests
{
    [Test]
    public void Safe_password_details_are_preserved_and_defensively_projected_onto_the_callers_field()
    {
        ValidationErrorDetail[] source =
        [
            new(ValidationErrorCodes.PasswordTooShort, new Dictionary<string, int> { ["min"] = 12 }),
            new(ValidationErrorCodes.PasswordRequiresDigit, new Dictionary<string, int>()),
        ];
        var result = new IdentityAccountValidationResult(false, source);

        source[0] = new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>());
        var firstProjection = result.PasswordErrorsFor("password");
        firstProjection["password"][0] = new ValidationErrorDetail(
            ValidationErrorCodes.Invalid,
            new Dictionary<string, int>());

        var secondProjection = result.PasswordErrorsFor("password");
        secondProjection["password"].Select(detail => detail.Code).ShouldBe([
            ValidationErrorCodes.PasswordTooShort,
            ValidationErrorCodes.PasswordRequiresDigit,
        ]);
        secondProjection["password"][0].Params["min"].ShouldBe(12);
    }

    [Test]
    public void A_provider_refusal_without_details_uses_only_the_generic_password_policy_fallback()
    {
        var result = new IdentityAccountValidationResult(false);

        var detail = result.PasswordErrorsFor("password")["password"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
        detail.Params.ShouldBeEmpty();
    }
}
