using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Models;

public sealed class ResultTests
{
    [Test]
    public void Success_has_a_value_and_no_error()
    {
        var result = Result<Guid>.Success(Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldNotBe(Guid.Empty);
        result.Error.ShouldBeNull();
    }

    [Test]
    public void Failure_has_one_typed_error_and_no_value()
    {
        var error = new ApplicationError("todo_item_concurrency_conflict", ApplicationErrorCategory.Conflict);
        var result = Result<Guid>.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Value.ShouldBe(Guid.Empty);
        result.Error.ShouldBeSameAs(error);
        result.Error!.Category.ShouldBe(ApplicationErrorCategory.Conflict);
    }

    [Test]
    public void Non_validation_errors_cannot_contain_field_errors()
    {
        Should.Throw<ArgumentException>(() => new ApplicationError(
            "not_found",
            ApplicationErrorCategory.NotFound,
            validationErrors: new Dictionary<string, ValidationErrorDetail[]>
            {
                ["id"] = [new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>())]
            }));
    }

    [Test]
    public void Result_categories_have_no_unexpected_escape_hatch()
    {
        Enum.GetNames<ApplicationErrorCategory>().ShouldNotContain("Unexpected");
    }

    [Test]
    public void Rate_limited_error_carries_an_explicit_retry_after_value()
    {
        var error = new ApplicationError("rate_limit_exceeded", ApplicationErrorCategory.RateLimited, retryAfterSeconds: 30);

        error.RetryAfterSeconds.ShouldBe(30);
    }

    [Test]
    public void Validation_errors_are_deep_copied_on_input_and_output()
    {
        var sourceParams = new Dictionary<string, int> { ["max"] = 18 };
        var sourceDetails = new[] { new ValidationErrorDetail(ValidationErrorCodes.TooLong, sourceParams) };
        var source = new Dictionary<string, ValidationErrorDetail[]> { ["NewPassword"] = sourceDetails };
        var error = new ApplicationError("validation_failed", ApplicationErrorCategory.Validation, validationErrors: source);

        sourceParams["max"] = 999;
        sourceDetails[0] = new ValidationErrorDetail(ValidationErrorCodes.Required, new Dictionary<string, int>());
        var firstRead = error.ValidationErrors;
        firstRead["NewPassword"][0] = new ValidationErrorDetail(ValidationErrorCodes.Invalid, new Dictionary<string, int>());

        var retained = error.ValidationErrors["NewPassword"].ShouldHaveSingleItem();
        retained.Code.ShouldBe(ValidationErrorCodes.TooLong);
        retained.Params["max"].ShouldBe(18);
    }
}
