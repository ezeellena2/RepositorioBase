using FluentValidation.Results;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace CleanArchitecture.Application.Common.Validation;

/// <summary>Stable public validation vocabulary. FluentValidation rule names and messages never cross the boundary.</summary>
public static class ValidationErrorCodes
{
    public const string Required = "required";
    public const string TooLong = "too_long";
    public const string UnsupportedValue = "unsupported_value";
    public const string PasswordPolicy = "password_policy";

    // A malformed or future server rule must not disclose FluentValidation implementation details or its message.
    public const string Invalid = "invalid";

    private const string SchemaResourceName = "CleanArchitecture.Application.validationErrorSchema.json";
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OwnedSchema = LoadSchema();

    /// <summary>The server-owned vocabulary and exact numeric parameter names shared with boundary consumers.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Schema => OwnedSchema;

    public static bool IsApproved(string? code) => code is not null && OwnedSchema.ContainsKey(code);

    public static ValidationErrorDetail FromFailure(ValidationFailure failure)
    {
        var code = IsApproved(failure.ErrorCode) ? failure.ErrorCode : Invalid;
        var parameters = code == TooLong
            ? NumericParameter(failure.FormattedMessagePlaceholderValues, "MaxLength", "max")
            : EmptyParameters();

        return new ValidationErrorDetail(code, parameters);
    }

    internal static (string Code, IReadOnlyDictionary<string, int> Params) Normalize(
        string? code,
        IReadOnlyDictionary<string, int>? parameters)
    {
        var safeCode = IsApproved(code) ? code! : Invalid;
        var safeParameters = SafeParameters(safeCode, parameters);

        // A parameterized code without its exact schema cannot be rendered truthfully. Preserve the refusal but
        // downgrade it to the generic safe code instead of emitting a broken interpolation contract.
        return OwnedSchema[safeCode].Count > 0 && safeParameters.Count == 0
            ? (Invalid, EmptyParameters())
            : (safeCode, safeParameters);
    }

    private static IReadOnlyDictionary<string, int> EmptyParameters() =>
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    private static IReadOnlyDictionary<string, int> SafeParameters(string? code, IReadOnlyDictionary<string, int>? parameters)
    {
        if (code is null || !OwnedSchema.TryGetValue(code, out var names) || names.Count == 0)
            return EmptyParameters();

        if (parameters?.Count != names.Count)
            return EmptyParameters();

        var copy = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!parameters.TryGetValue(name, out var value) || value <= 0)
                return EmptyParameters();
            copy.Add(name, value);
        }

        return new ReadOnlyDictionary<string, int>(copy);
    }

    private static IReadOnlyDictionary<string, int> NumericParameter(
        IReadOnlyDictionary<string, object>? placeholders,
        string placeholderName,
        string parameterName)
    {
        if (placeholders?.TryGetValue(placeholderName, out var value) != true || value is not int number)
        {
            return EmptyParameters();
        }

        return SafeParameters(TooLong, new Dictionary<string, int> { [parameterName] = number });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadSchema()
    {
        using var stream = typeof(ValidationErrorCodes).Assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException("The validation error schema is missing.");
        var schema = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException("The validation error schema is invalid.");

        var expectedCodes = new[] { Required, TooLong, UnsupportedValue, PasswordPolicy, Invalid };
        if (!schema.Keys.Order(StringComparer.Ordinal).SequenceEqual(expectedCodes.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("The validation error constants and schema do not match.");

        var copy = schema.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.Distinct(StringComparer.Ordinal).ToArray()),
            StringComparer.Ordinal);
        if (copy.Any(pair => pair.Key.Length == 0
            || pair.Value.Any(name => string.IsNullOrWhiteSpace(name))
            || pair.Value.Count != schema[pair.Key].Length))
        {
            throw new InvalidOperationException("The validation error schema contains an invalid code or parameter name.");
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
    }
}

/// <summary>Safe validation metadata rendered by the SPA from its own catalog.</summary>
public sealed record ValidationErrorDetail
{
    public ValidationErrorDetail(string code, IReadOnlyDictionary<string, int>? parameters)
    {
        var safe = ValidationErrorCodes.Normalize(code, parameters);
        Code = safe.Code;
        Params = safe.Params;
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, int> Params { get; }
}
