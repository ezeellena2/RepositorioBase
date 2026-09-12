using System.Collections.ObjectModel;

using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.Common.Models;

/// <summary>
/// An expected business failure. Its code is a stable, public-safe machine
/// identifier; it deliberately never carries provider exception text.
/// </summary>
public sealed class ApplicationError
{
    private readonly IReadOnlyDictionary<string, ValidationErrorDetail[]> _validationErrors;

    public ApplicationError(
        string code,
        ApplicationErrorCategory category,
        string? detail = null,
        IReadOnlyDictionary<string, ValidationErrorDetail[]>? validationErrors = null,
        int? retryAfterSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("An application error requires a stable code.", nameof(code));
        }

        if (category != ApplicationErrorCategory.Validation && validationErrors is { Count: > 0 })
        {
            throw new ArgumentException("Field errors are only valid for validation failures.", nameof(validationErrors));
        }

        if (retryAfterSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds), "Retry-After must be positive when supplied.");
        }

        if (retryAfterSeconds is not null && category is not (ApplicationErrorCategory.RateLimited or ApplicationErrorCategory.Unavailable))
        {
            throw new ArgumentException("Retry-After is only valid for rate-limited and unavailable failures.", nameof(retryAfterSeconds));
        }

        Code = code.Trim();
        Category = category;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        _validationErrors = validationErrors is null
            ? new ReadOnlyDictionary<string, ValidationErrorDetail[]>(new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal))
            : CopyValidationErrors(validationErrors);
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }

    public ApplicationErrorCategory Category { get; }

    /// <summary>Optional public-safe detail. It must never contain provider diagnostics or secrets.</summary>
    public string? Detail { get; }

    /// <summary>A defensive projection keeps the error immutable even though the wire contract uses arrays.</summary>
    public IReadOnlyDictionary<string, ValidationErrorDetail[]> ValidationErrors => CopyValidationErrors(_validationErrors);

    public int? RetryAfterSeconds { get; }

    private static IReadOnlyDictionary<string, ValidationErrorDetail[]> CopyValidationErrors(IReadOnlyDictionary<string, ValidationErrorDetail[]> errors)
    {
        var copy = new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal);
        foreach (var (field, details) in errors)
        {
            if (string.IsNullOrWhiteSpace(field)
                || details is null
                || details.Length == 0
                || details.Any(detail => detail is null || !ValidationErrorCodes.IsApproved(detail.Code)))
            {
                throw new ArgumentException("Validation errors must contain a field and approved safe details.", nameof(errors));
            }

            copy.Add(field, [.. details]);
        }

        return new ReadOnlyDictionary<string, ValidationErrorDetail[]>(copy);
    }
}
