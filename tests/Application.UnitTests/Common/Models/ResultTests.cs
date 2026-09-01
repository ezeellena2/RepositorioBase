using CleanArchitecture.Application.Common.Models;
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
            validationErrors: new Dictionary<string, string[]> { ["id"] = ["missing"] }));
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
}
