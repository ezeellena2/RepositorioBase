using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Architecture;

public sealed class LocalizationContractTests
{
    [Test]
    public void Phase_four_models_own_their_language_state()
    {
        typeof(ApplicationUser).GetProperty("PreferredLanguage").ShouldNotBeNull();
        typeof(Invitation).GetProperty("Language").ShouldNotBeNull();
        typeof(PlatformAdminInvitation).GetProperty("Language").ShouldNotBeNull();
        typeof(PendingRegistrationIntent).GetProperty("Language").ShouldNotBeNull();
        typeof(PendingPersonalIntent).GetProperty("Language").ShouldNotBeNull();
        typeof(OutboxMessage).GetProperty("DeliveryLanguage").ShouldNotBeNull();
    }

    [Test]
    public void Backend_registry_matches_the_spa_language_registry_and_shipped_default()
    {
        using var languages = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/src/i18n/languages.json")));
        using var appsettings = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/appsettings.json")));

        languages.RootElement.GetProperty("source").GetString().ShouldBe(LocalizationRegistry.SourceLanguage);
        languages.RootElement.GetProperty("default").GetString().ShouldBe(LocalizationRegistry.DefaultLanguage);
        languages.RootElement.GetProperty("supported").EnumerateArray().Select(value => value.GetString()).ShouldBe(LocalizationRegistry.SupportedLanguages);
        languages.RootElement.GetProperty("inProgress").EnumerateArray().Select(value => value.GetString()).ShouldBe(LocalizationRegistry.InProgressLanguages);
        var journeyLanguages = languages.RootElement.GetProperty("journeys").EnumerateArray()
            .Select(value => value.GetProperty("language").GetString().ShouldNotBeNull())
            .ToArray();
        journeyLanguages.Distinct(StringComparer.Ordinal).Count().ShouldBe(journeyLanguages.Length);
        journeyLanguages.ShouldBe(
            LocalizationRegistry.SupportedLanguages.Where(language => language != LocalizationRegistry.SourceLanguage));
        foreach (var language in journeyLanguages)
        {
            CultureInfo.GetCultureInfo(language).Name.ShouldBe(language);
        }
        appsettings.RootElement.GetProperty("Localization").GetProperty("DefaultLanguage").GetString()
            .ShouldBe(LocalizationRegistry.DefaultLanguage);
    }

    [Test]
    public void Phase_six_unused_key_gate_is_read_only_and_limited_to_static_namespaces()
    {
        using var package = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/package.json")));
        var configuration = File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/i18next.config.js"));
        var namespaces = File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/src/i18n/staticUnusedNamespaces.js"));
        var runner = File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/scripts/check-unused-i18n.mjs"));
        var workflow = File.ReadAllText(GetRepositoryPath(".github/workflows/build.yml"));

        package.RootElement.GetProperty("scripts").GetProperty("i18n:unused").GetString()
            .ShouldBe("node ./scripts/check-unused-i18n.mjs");
        namespaces.ShouldContain("['common', 'identity', 'platform']");
        namespaces.ShouldNotContain("'errors'");
        namespaces.ShouldNotContain("'enums'");
        configuration.ShouldContain("ignoreNamespaces: ['errors', 'enums']");
        configuration.ShouldContain("preservePatterns: ['common:language.*']");
        configuration.ShouldContain("ignore: ['src/**/*.test.{js,jsx}', 'src/test/**']");
        runner.ShouldContain("'status', languages.source, '--unused', '--namespace', namespace");
        workflow.ShouldContain("- name: Reject unused static localization keys");
        workflow.ShouldContain("run: npm run i18n:unused");
    }

    [Test]
    public void Localization_journey_is_generated_from_the_registry_and_reads_expected_copy_from_catalogs()
    {
        var feature = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/Features/Localization.feature"));
        var page = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/Pages/LocalizationPage.cs"));
        var project = XDocument.Load(GetRepositoryPath("tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj"));
        var centralPackages = XDocument.Load(GetRepositoryPath("Directory.Packages.props"));

        feature.ShouldContain("@DataSource:../../../src/Web/ClientApp/src/i18n/languages.json");
        feature.ShouldContain("@DataSet:journeys");
        feature.ShouldContain("<language>");
        feature.ShouldNotContain("Spanish");
        feature.ShouldNotContain("Español");
        page.ShouldContain("src/Web/ClientApp/src/i18n/languages.json");
        page.ShouldContain("src/Web/ClientApp/src/i18n/locales/{language}/common.json");
        page.ShouldContain("src/Web/ClientApp/src/i18n/locales/{language}/identity.json");
        page.ShouldNotContain("Iniciar sesión");
        page.ShouldNotContain("Correo electrónico");
        project.Descendants("PackageReference")
            .Single(reference => reference.Attribute("Include")?.Value == "Reqnroll.ExternalData")
            .Attribute("Version").ShouldBeNull();
        centralPackages.Descendants("PackageVersion")
            .Single(reference => reference.Attribute("Include")?.Value == "Reqnroll.ExternalData")
            .Attribute("Version")?.Value.ShouldBe("3.3.4");
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

    [Test]
    public void Identity_email_catalog_covers_every_phase_four_delivery_variant()
    {
        var entries = ReadEntries(GetRepositoryPath("src/Infrastructure/Localization/Emails.resx"));
        entries.Keys.ShouldBe(
        [
            "AccountReactivationBody", "AccountReactivationSubject",
            "AccountAdministrativelySuspendedBody", "AccountAdministrativelySuspendedSubject",
            "ConfirmationBody", "ConfirmationSubject",
            "InvitationBody", "InvitationSubject",
            "OrganizationRegistrationConfirmationBody", "OrganizationRegistrationConfirmationSubject",
            "OrganizationRegistrationSignInBody", "OrganizationRegistrationSignInSubject",
            "PasswordRecoveryBody", "PasswordRecoverySubject",
            "PersonalRegistrationConfirmationBody", "PersonalRegistrationConfirmationSubject",
            "PersonalRegistrationSignInBody", "PersonalRegistrationSignInSubject",
            "PlatformAdministratorInvitationBody", "PlatformAdministratorInvitationSubject",
            "PlatformConfirmationBody", "PlatformConfirmationSubject",
            "PlatformOwnerInvitationBody", "PlatformOwnerInvitationSubject",
            "PlatformSignInBody", "PlatformSignInSubject",
            "AccountReactivatedBody", "AccountReactivatedSubject",
            "AccountSelfDeactivatedBody", "AccountSelfDeactivatedSubject",
            "SignInNoticeBody", "SignInNoticeSubject"
        ], ignoreOrder: true);
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
