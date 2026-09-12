using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Validation;
using FluentValidation.Results;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Exceptions;

public class ValidationExceptionTests
{
    [Test]
    public void DefaultConstructorCreatesAnEmptyErrorDictionary()
    {
        var actual = new ValidationException().Errors;

        actual.Keys.ShouldBeEmpty();
    }

    [Test]
    public void SingleValidationFailureCarriesAnOwnedCodeAndOnlySafeTemplateMetadata()
    {
        var failures = new List<ValidationFailure>
            {
                new ValidationFailure("Age", "must be at most 18 characters")
                {
                    ErrorCode = ValidationErrorCodes.TooLong,
                    FormattedMessagePlaceholderValues = new Dictionary<string, object>
                    {
                        ["MaxLength"] = 18,
                        ["PropertyValue"] = "sensitive value",
                        ["DNI"] = 12345678,
                        ["PIN"] = 4321,
                        ["unknown"] = 7,
                    }
                },
            };

        var actual = new ValidationException(failures).Errors;

        actual.Keys.ShouldBe(new string[] { "Age" });
        var detail = actual["Age"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.TooLong);
        detail.Params.Count.ShouldBe(1);
        detail.Params["max"].ShouldBe(18);
    }

    [Test]
    public void UnapprovedCodesAndMessagesNeverCrossTheValidationBoundary()
    {
        var failures = new List<ValidationFailure>
            {
                new ValidationFailure("Email", "alice@example.test is not valid") { ErrorCode = "CustomValidator" },
            };

        var actual = new ValidationException(failures).Errors;

        var detail = actual["Email"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.Invalid);
        detail.Params.ShouldBeEmpty();
    }

    [TestCase(null)]
    [TestCase("18")]
    [TestCase(0)]
    [TestCase(-1)]
    public void Malformed_too_long_placeholders_from_FluentValidation_downgrade_safely(object? maxLength)
    {
        var failure = new ValidationFailure("Name", "provider prose")
        {
            ErrorCode = ValidationErrorCodes.TooLong,
            FormattedMessagePlaceholderValues = new Dictionary<string, object>
            {
                ["MaxLength"] = maxLength!,
                ["PropertyValue"] = 42,
                ["DNI"] = 12345678,
                ["PIN"] = 4321,
                ["unknown"] = 7,
            }
        };

        var detail = new ValidationException([failure]).Errors["Name"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.Invalid);
        detail.Params.ShouldBeEmpty();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Non_positive_too_long_metadata_downgrades_to_the_generic_safe_code(int max)
    {
        var detail = new ValidationErrorDetail(
            ValidationErrorCodes.TooLong,
            new Dictionary<string, int> { ["max"] = max });

        detail.Code.ShouldBe(ValidationErrorCodes.Invalid);
        detail.Params.ShouldBeEmpty();
    }

    [Test]
    public void Missing_null_extra_or_arbitrary_too_long_metadata_downgrades_safely()
    {
        IReadOnlyDictionary<string, int>?[] malformed =
        [
            null,
            new Dictionary<string, int>(),
            new Dictionary<string, int> { ["PropertyValue"] = 42 },
            new Dictionary<string, int> { ["DNI"] = 12345678 },
            new Dictionary<string, int> { ["PIN"] = 1234 },
            new Dictionary<string, int> { ["unknown"] = 7 },
            new Dictionary<string, int> { ["max"] = 256, ["PropertyValue"] = 42 },
        ];

        foreach (var parameters in malformed)
        {
            var detail = new ValidationErrorDetail(ValidationErrorCodes.TooLong, parameters);
            detail.Code.ShouldBe(ValidationErrorCodes.Invalid);
            detail.Params.ShouldBeEmpty();
        }
    }

    [Test]
    public void Codes_without_parameters_drop_every_arbitrary_numeric_key()
    {
        var parameters = new Dictionary<string, int>
        {
            ["PropertyValue"] = 42,
            ["DNI"] = 12345678,
            ["PIN"] = 1234,
            ["unknown"] = 7,
        };

        foreach (var code in new[]
                 {
                     ValidationErrorCodes.Required,
                     ValidationErrorCodes.UnsupportedValue,
                     ValidationErrorCodes.PasswordPolicy,
                     ValidationErrorCodes.Invalid,
                 })
        {
            var detail = new ValidationErrorDetail(code, parameters);
            detail.Code.ShouldBe(code);
            detail.Params.ShouldBeEmpty();
        }
    }

    [Test]
    public void Parameters_are_deep_copied_and_cannot_be_changed_after_construction()
    {
        var source = new Dictionary<string, int> { ["max"] = 18 };
        var detail = new ValidationErrorDetail(ValidationErrorCodes.TooLong, source);

        source["max"] = 999;
        detail.Params["max"].ShouldBe(18);
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, int>)detail.Params)["max"] = 7);
        detail.Params["max"].ShouldBe(18);
    }

    [Test]
    public void A_failure_without_the_required_too_long_placeholder_becomes_generic_and_never_carries_other_placeholders()
    {
        var failure = new ValidationFailure("Pin", "provider prose")
        {
            ErrorCode = ValidationErrorCodes.TooLong,
            FormattedMessagePlaceholderValues = new Dictionary<string, object>
            {
                ["PropertyValue"] = 1234,
                ["DNI"] = 12345678,
                ["PIN"] = 4321,
            }
        };

        var detail = new ValidationException([failure]).Errors["Pin"].ShouldHaveSingleItem();
        detail.Code.ShouldBe(ValidationErrorCodes.Invalid);
        detail.Params.ShouldBeEmpty();
    }
}
