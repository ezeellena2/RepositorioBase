namespace CleanArchitecture.Web.Localization;

/// <summary>
/// The language registry is code-owned so every runtime boundary negotiates the same admitted set.
/// Deployment configuration may choose only a member of <see cref="SupportedLanguages"/> as its default.
/// </summary>
public static class LocalizationRegistry
{
    public const string SourceLanguage = "en";
    public const string DefaultLanguage = SourceLanguage;

    public static IReadOnlyList<string> SupportedLanguages { get; } = [SourceLanguage];
    public static IReadOnlyList<string> InProgressLanguages { get; } = ["es"];

    public static bool IsSupported(string? language) =>
        language is not null && SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

    public static string RequireSupportedDefault(string? configuredLanguage)
    {
        var language = configuredLanguage ?? DefaultLanguage;
        if (!IsSupported(language))
        {
            throw new InvalidOperationException(
                "Localization:DefaultLanguage must be a supported language.");
        }

        return language;
    }
}
