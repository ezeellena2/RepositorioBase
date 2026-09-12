using FluentValidation.Results;

using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, ValidationErrorDetail[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, ValidationErrorCodes.FromFailure)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
    }

    public IDictionary<string, ValidationErrorDetail[]> Errors { get; }
}
