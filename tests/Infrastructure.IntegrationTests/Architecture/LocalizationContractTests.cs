using CleanArchitecture.Web.Localization;
using System.Text.Json;
using System.Xml.Linq;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Architecture;

public sealed class LocalizationContractTests
{
    [Test]
    public void Backend_registry_matches_the_spa_language_registry_and_shipped_default()
    {
        using var languages = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/src/i18n/languages.json")));
        using var appsettings = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/appsettings.json")));

        languages.RootElement.GetProperty("source").GetString().ShouldBe(LocalizationRegistry.SourceLanguage);
        languages.RootElement.GetProperty("default").GetString().ShouldBe(LocalizationRegistry.DefaultLanguage);
        languages.RootElement.GetProperty("supported").EnumerateArray().Select(value => value.GetString()).ShouldBe(LocalizationRegistry.SupportedLanguages);
        languages.RootElement.GetProperty("inProgress").EnumerateArray().Select(value => value.GetString()).ShouldBe(LocalizationRegistry.InProgressLanguages);
        appsettings.RootElement.GetProperty("Localization").GetProperty("DefaultLanguage").GetString()
            .ShouldBe(LocalizationRegistry.DefaultLanguage);
    }

    [Test]
    public void Localized_resources_match_their_neutral_source_names_values_and_placeholders()
    {
        var resourceFiles = Directory.EnumerateFiles(GetRepositoryPath("src"), "*.resx", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Name = Path.GetFileNameWithoutExtension(path) })
            .ToList();

        foreach (var source in resourceFiles.Where(resource => !IsCultureSpecific(resource.Name)))
        {
            var sourceEntries = ReadEntries(source.Path);
            foreach (var culture in LocalizationRegistry.SupportedLanguages.Where(language => language != LocalizationRegistry.SourceLanguage))
            {
                var localizedPath = Path.Combine(Path.GetDirectoryName(source.Path)!, $"{source.Name}.{culture}.resx");
                File.Exists(localizedPath).ShouldBeTrue($"Missing resource catalog: {localizedPath}");

                var localizedEntries = ReadEntries(localizedPath);
                localizedEntries.Keys.ShouldBe(sourceEntries.Keys);
                foreach (var (key, sourceValue) in sourceEntries)
                {
                    localizedEntries[key].ShouldNotBeNullOrWhiteSpace($"Localized resource value is empty: {localizedPath}::{key}");
                    Placeholders(localizedEntries[key]).ShouldBe(Placeholders(sourceValue), $"Placeholder mismatch: {localizedPath}::{key}");
                }
            }
        }
    }

    private static Dictionary<string, string> ReadEntries(string path) =>
        XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                entry => entry.Attribute("name")!.Value,
                entry => entry.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static IReadOnlyList<string> Placeholders(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, "\\{(\\d+)\\}")
            .Select(match => match.Groups[1].Value)
            .Order()
            .ToList();

    private static bool IsCultureSpecific(string resourceName)
    {
        var lastSegment = resourceName.LastIndexOf('.');
        return lastSegment >= 0 && resourceName[(lastSegment + 1)..].Length is 2 or 5;
    }

    private static string GetRepositoryPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
