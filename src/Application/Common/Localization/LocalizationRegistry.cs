namespace CleanArchitecture.Application.Common.Localization;

/// <summary>
/// The code-owned language registry shared by the interactive Web boundary and background delivery.
/// Deployment configuration may choose only a supported language as its default.
/// </summary>
public static class LocalizationRegistry
{
    public const string SourceLanguage = "en";
    public const string DefaultLanguage = SourceLanguage;

    public static IReadOnlyList<string> SupportedLanguages { get; } = [SourceLanguage, "es"];
    public static IReadOnlyList<string> InProgressLanguages { get; } = [];

    public static bool IsSupported(string? language) =>
        language is not null && SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

    public static bool IsCanonicalSupported(string? language) =>
        language is not null && SupportedLanguages.Contains(language, StringComparer.Ordinal);

    public static string? SupportedCanonicalOrNull(string? language) =>
        SupportedLanguages.SingleOrDefault(candidate => string.Equals(candidate, language, StringComparison.Ordinal));

    public static string RequireSupportedDefault(string? configuredLanguage)
    {
        var language = configuredLanguage ?? DefaultLanguage;
        if (!IsCanonicalSupported(language))
        {
            throw new InvalidOperationException(
                "Localization:DefaultLanguage must be a canonical supported language.");
        }

        return language;
    }
}

/// <summary>The validated deployment fallback shared by Web and the outbox worker.</summary>
public sealed record LocalizationSettings(string DefaultLanguage);
