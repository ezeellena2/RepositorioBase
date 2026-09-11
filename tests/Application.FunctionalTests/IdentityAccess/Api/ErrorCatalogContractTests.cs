using System.Text.Json;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class ErrorCatalogContractTests : TestBase
{
    [Test]
    public async Task Every_advertised_problem_code_has_a_trimmed_error_message_in_each_supported_language()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var advertisedCodes = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.TryGetProperty("responses", out _))
            .SelectMany(operation => operation.Value.GetProperty("responses").EnumerateObject())
            .Where(responseDefinition => responseDefinition.Value.TryGetProperty("x-problem-codes", out _))
            .SelectMany(responseDefinition => responseDefinition.Value.GetProperty("x-problem-codes").EnumerateArray())
            .Select(code => code.GetString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        advertisedCodes.ShouldNotBeEmpty("the served OpenAPI document must declare problem codes to localize");

        using var languages = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/src/i18n/languages.json")));
        var supportedLanguages = languages.RootElement.GetProperty("supported")
            .EnumerateArray()
            .Select(language => language.GetString())
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(language => language!)
            .ToArray();

        supportedLanguages.ShouldNotBeEmpty("at least one language must be supported");

        foreach (var language in supportedLanguages)
        {
            using var errors = JsonDocument.Parse(File.ReadAllText(
                GetRepositoryPath($"src/Web/ClientApp/src/i18n/locales/{language}/errors.json")));

            foreach (var code in advertisedCodes)
            {
                errors.RootElement.TryGetProperty(code, out var message).ShouldBeTrue(
                    $"{language}/errors.json must contain the advertised problem code '{code}'.");
                message.ValueKind.ShouldBe(JsonValueKind.String,
                    $"{language}/errors.json::{code} must be a top-level string.");

                var value = message.GetString() ?? string.Empty;
                value.ShouldNotBeNullOrWhiteSpace($"{language}/errors.json::{code} must not be empty.");
                value.ShouldBe(value.Trim(), $"{language}/errors.json::{code} must be trimmed.");
            }
        }
    }

    private static string GetRepositoryPath(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "artifacts", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.Combine(directory.Parent!.FullName, relativePath));
            }
        }

        throw new InvalidOperationException($"No artifacts directory contains {AppContext.BaseDirectory}.");
    }
}
