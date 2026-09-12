using CleanArchitecture.Application.Common.Validation;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Translates provider-owned password failures into the server-owned public vocabulary. Identity descriptions are
/// deliberately ignored: they are configurable prose, not a stable or safe boundary contract.
/// </summary>
internal static class IdentityPasswordValidationDetails
{
    public static IReadOnlyList<ValidationErrorDetail> From(
        IEnumerable<IdentityError> errors,
        PasswordOptions options)
    {
        var mapped = errors
            .Select(error => Map(error.Code, options))
            .OrderBy(detail => detail.Code, StringComparer.Ordinal)
            .ThenBy(detail => detail.Params.Values.SingleOrDefault())
            .ToArray();

        var unique = new List<ValidationErrorDetail>(mapped.Length);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detail in mapped)
        {
            var signature = detail.Params.Count == 0
                ? detail.Code
                : $"{detail.Code}:{string.Join(',', detail.Params.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))}";
            if (signatures.Add(signature)) unique.Add(detail);
        }

        return unique.AsReadOnly();
    }

    private static ValidationErrorDetail Map(string? providerCode, PasswordOptions options) => providerCode switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) when options.RequiredLength > 0 =>
            Detail(ValidationErrorCodes.PasswordTooShort, "min", options.RequiredLength),
        nameof(IdentityErrorDescriber.PasswordRequiresUpper) => Detail(ValidationErrorCodes.PasswordRequiresUppercase),
        nameof(IdentityErrorDescriber.PasswordRequiresLower) => Detail(ValidationErrorCodes.PasswordRequiresLowercase),
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) => Detail(ValidationErrorCodes.PasswordRequiresDigit),
        nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => Detail(ValidationErrorCodes.PasswordRequiresSymbol),
        nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) when options.RequiredUniqueChars > 0 =>
            Detail(ValidationErrorCodes.PasswordRequiresUniqueCharacters, "min", options.RequiredUniqueChars),
        _ => Detail(ValidationErrorCodes.PasswordPolicy)
    };

    private static ValidationErrorDetail Detail(string code) => new(code, new Dictionary<string, int>());

    private static ValidationErrorDetail Detail(string code, string parameter, int value) =>
        new(code, new Dictionary<string, int> { [parameter] = value });
}
