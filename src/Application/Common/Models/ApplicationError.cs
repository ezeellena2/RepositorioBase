using System.Collections.ObjectModel;

namespace CleanArchitecture.Application.Common.Models;

/// <summary>
/// An expected business failure. Its code is a stable, public-safe machine
/// identifier; it deliberately never carries provider exception text.
/// </summary>
public sealed class ApplicationError
{
    public ApplicationError(
        string code,
        ApplicationErrorCategory category,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
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

        if (retryAfterSeconds is not null && category != ApplicationErrorCategory.RateLimited)
        {
            throw new ArgumentException("Retry-After is only valid for rate-limited failures.", nameof(retryAfterSeconds));
        }

        Code = code.Trim();
        Category = category;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        ValidationErrors = validationErrors is null
            ? new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(StringComparer.Ordinal))
            : CopyValidationErrors(validationErrors);
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }

    public ApplicationErrorCategory Category { get; }

    /// <summary>Optional public-safe detail. It must never contain provider diagnostics or secrets.</summary>
    public string? Detail { get; }

    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public int? RetryAfterSeconds { get; }

    private static IReadOnlyDictionary<string, string[]> CopyValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var copy = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (field, messages) in errors)
        {
            if (string.IsNullOrWhiteSpace(field) || messages is null || messages.Length == 0 || messages.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Validation errors must contain non-empty field names and messages.", nameof(errors));
            }

            copy.Add(field, [.. messages]);
        }

        return new ReadOnlyDictionary<string, string[]>(copy);
    }
}
