using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Xml.Linq;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Architecture;

public sealed class StackBaselineTests
{
    [Test]
    public void ApplicationDbContext_uses_the_postgresql_provider()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Test]
    public void Web_pipeline_does_not_enable_an_open_cors_policy()
    {
        var programPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Web", "Program.cs"));

        File.ReadAllText(programPath).ShouldNotContain("AllowAnyOrigin");
    }

    [TestCase("src/Infrastructure/Infrastructure.csproj")]
    [TestCase("src/AppHost/AppHost.csproj")]
    [TestCase("tests/TestAppHost/TestAppHost.csproj")]
    public void Active_projects_reference_only_unconditional_postgresql_packages(string relativeProjectPath)
    {
        var project = XDocument.Load(GetRepositoryPath(relativeProjectPath));
        var packageReferences = project.Descendants("PackageReference").ToList();

        foreach (var packageReference in packageReferences.Where(IsDatabaseProviderPackage))
        {
            packageReference.Attribute("Condition").ShouldBeNull();
            packageReference.Attribute("Include")?.Value.ShouldContain("PostgreSQL");
        }
    }

    [Test]
    public void Web_project_generates_openapi_and_publishes_the_selected_react_client()
    {
        var project = XDocument.Load(GetRepositoryPath("src/Web/Web.csproj"));
        using var package = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/package.json")));

        project.Descendants("OpenApiGenerateDocumentsOnBuild").Single().Value.ShouldBe("true");

        var publishTarget = project.Descendants("Target").Single(target => target.Attribute("Name")?.Value == "PublishRunWebpack");
        publishTarget.Attribute("Condition")?.Value.ShouldContain("$(ClientFramework)");
        publishTarget.Descendants("Exec").Single(exec => exec.Attribute("Command")?.Value == "npm run build")
            .Attribute("Condition")?.Value.ShouldContain("$(ClientFramework)");
        var scripts = package.RootElement.GetProperty("scripts");
        scripts.GetProperty("generate-api").GetString().ShouldBe("nswag run /runtime:Net100");
        scripts.GetProperty("prebuild").GetString().ShouldBe("npm run generate-api");
        scripts.GetProperty("prestart").GetString().ShouldBe("npm run generate-api");
    }

    [Test]
    public void Web_publish_contract_uses_each_client_build_output_directory()
    {
        var project = XDocument.Load(GetRepositoryPath("src/Web/Web.csproj"));
        var viteConfiguration = File.ReadAllText(GetRepositoryPath("src/Web/ClientApp/vite.config.ts"));
        using var angularConfiguration = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath("src/Web/ClientApp-Angular/angular.json")));

        var publishTarget = project.Descendants("Target").Single(target => target.Attribute("Name")?.Value == "PublishRunWebpack");
        var reactPublishFiles = publishTarget.Descendants("DistFiles")
            .Single(item => item.Attribute("Condition")?.Value.Contains("React", StringComparison.Ordinal) == true);
        var angularPublishFiles = publishTarget.Descendants("DistFiles")
            .Single(item => item.Attribute("Condition")?.Value.Contains("Angular", StringComparison.Ordinal) == true);

        viteConfiguration.ShouldContain("outDir: 'build'");
        reactPublishFiles.Attribute("Include")?.Value.ShouldBe("$(SpaRoot)build\\**");
        angularConfiguration.RootElement
            .GetProperty("projects")
            .GetProperty("CleanArchitecture.Web")
            .GetProperty("architect")
            .GetProperty("build")
            .GetProperty("options")
            .GetProperty("outputPath")
            .GetString()
            .ShouldBe("dist");
        angularPublishFiles.Attribute("Include")?.Value.ShouldBe("$(SpaRoot)dist\\browser\\**;");
    }

    [Test]
    public void Acceptance_harness_provisions_an_in_memory_confirmed_account_and_signs_in_through_the_identity_session_endpoint()
    {
        var setup = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/AspireSetup.cs"));
        var credentials = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/AcceptanceTestCredentials.cs"));
        var loginSteps = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/StepDefinitions/LoginStepDefinitions.cs"));

        setup.ShouldContain("AcceptanceTestCredentials.CreateAsync(App, cancellationToken)");
        credentials.ShouldContain("/api/identity/antiforgery");
        credentials.ShouldContain("/api/identity/sessions");
        credentials.ShouldContain("EmailConfirmed");
        credentials.ShouldNotContain("/api/Users/");
        credentials.ShouldNotContain("Environment.GetEnvironmentVariable");
        credentials.ShouldNotContain("CLEANARCHITECTURE_ACCEPTANCE_TEST_");
        credentials.ShouldNotContain("administrator@localhost");
        loginSteps.ShouldContain("AcceptanceTestCredentials.SignInAsync");
    }

    [Test]
    public void Template_includes_postgresql_migrations_for_every_generated_variant()
    {
        using var template = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(".template.config/template.json")));
        var source = template.RootElement.GetProperty("sources")[0];
        var exclusions = source.GetProperty("exclude").EnumerateArray().Select(element => element.GetString()).ToList();

        exclusions.ShouldNotContain("src/Infrastructure/Data/Migrations/**");

        File.ReadAllText(GetRepositoryPath("CleanArchitecture.nuspec")).ShouldContain("<file src=\"build\\*.props\" target=\"content\\build\" />");
    }

    [Test]
    public void Template_supports_only_postgresql_and_includes_the_baseline_migration()
    {
        using var template = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(".template.config/template.json")));
        var symbols = template.RootElement.GetProperty("symbols");
        var database = symbols.GetProperty("Database");

        database.GetProperty("defaultValue").GetString().ShouldBe("postgresql");
        database.GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString())
            .ShouldBe(["postgresql"]);
        symbols.TryGetProperty("UseSqlite", out _).ShouldBeFalse();
        symbols.TryGetProperty("UseSqlServer", out _).ShouldBeFalse();

        var templateText = File.ReadAllText(GetRepositoryPath(".template.config/template.json"));
        templateText.ShouldNotContain("Sqlite");
        templateText.ShouldNotContain("SQLServer");
        templateText.ShouldNotContain("UsePostgreSQL");

        File.Exists(GetRepositoryPath("src/Infrastructure/Data/Migrations/20260831183429_BaselinePostgreSql.cs")).ShouldBeTrue();
        File.Exists(GetRepositoryPath("src/Infrastructure/Data/Migrations/20260831183429_BaselinePostgreSql.Designer.cs")).ShouldBeTrue();
        File.Exists(GetRepositoryPath("build/DatabaseProvider.props")).ShouldBeFalse();
        File.Exists(GetRepositoryPath("build/DatabaseProvider.PostgreSQL.props")).ShouldBeFalse();
        File.Exists(GetRepositoryPath("build/DatabaseProvider.Sqlite.props")).ShouldBeFalse();
        File.Exists(GetRepositoryPath("build/DatabaseProvider.SqlServer.props")).ShouldBeFalse();
        File.Exists(GetRepositoryPath("src/Web/appsettings.SQLite.json")).ShouldBeFalse();
        File.Exists(GetRepositoryPath("src/Web/appsettings.SQLServer.json")).ShouldBeFalse();

        foreach (var relativePath in new[]
        {
            "Directory.Build.props",
            "Directory.Packages.props",
            "src/Infrastructure/Infrastructure.csproj",
            "src/AppHost/AppHost.csproj",
            "tests/TestAppHost/TestAppHost.csproj"
        })
        {
            var source = File.ReadAllText(GetRepositoryPath(relativePath));
            source.ShouldNotContain("DatabaseProvider");
            source.ShouldNotContain("SqlServer");
            source.ShouldNotContain("Sqlite");
        }
    }

    [Test]
    public void Repository_does_not_retain_stale_scaffold_references_or_fixed_acceptance_credentials()
    {
        var retiredReactClient = string.Concat("ClientApp", "-React");
        var retiredAdministratorEmail = string.Concat("administrator", "@localhost");
        var retiredAdministratorPassword = string.Concat("Administrator", "1!");

        foreach (var relativePath in new[]
        {
            "build/build.ps1",
            ".github/workflows/build.yml",
            ".github/workflows/test-templates.yml",
            ".gitignore",
            "src/Web/Web.http",
            "src/Web/Web-webapi.http",
            "tests/Web.AcceptanceTests/StepDefinitions/LoginStepDefinitions.cs"
        })
        {
            var source = File.ReadAllText(GetRepositoryPath(relativePath));
            source.ShouldNotContain(retiredReactClient);
            source.ShouldNotContain(retiredAdministratorEmail);
            source.ShouldNotContain(retiredAdministratorPassword);
        }

        var buildScript = File.ReadAllText(GetRepositoryPath("build/build.ps1"));
        buildScript.ShouldContain("./src/Web/ClientApp-Angular");

        var credentials = File.ReadAllText(GetRepositoryPath("tests/Web.AcceptanceTests/AcceptanceTestCredentials.cs"));
        credentials.ShouldNotContain("CLEANARCHITECTURE_ACCEPTANCE_TEST_");
        credentials.ShouldNotContain("Environment.GetEnvironmentVariable");

        var adr = File.ReadAllText(GetRepositoryPath("docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md"));
        adr.ShouldContain("Before Tasks 1 and 2");

        var plan = File.ReadAllText(GetRepositoryPath("docs/superpowers/plans/2026-08-31-identity-access-foundation.md"));
        plan.ShouldContain("IA-002 and IA-003 are already `Complete`");
        plan.ShouldNotContain("move IA-002 from `Blocked` to `Ready`");
    }

    [Test]
    public void Database_migration_policy_skips_only_the_openapi_document_generator()
    {
        DatabaseMigrationExecutionPolicy.IsOpenApiDocumentGeneration(
            "Microsoft.Extensions.ApiDescription.Tool.Commands.GetDocumentCommandWorker+NoopServer").ShouldBeTrue();

        DatabaseMigrationExecutionPolicy.IsOpenApiDocumentGeneration(
            "Microsoft.AspNetCore.TestHost.TestServer").ShouldBeFalse();
        DatabaseMigrationExecutionPolicy.ShouldMigrate(
            "Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl").ShouldBeTrue();
    }

    private static string GetRepositoryPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));

    private static bool IsDatabaseProviderPackage(XElement packageReference)
    {
        var packageId = packageReference.Attribute("Include")?.Value;
        return packageId?.Contains("Npgsql", StringComparison.Ordinal) == true ||
            packageId?.Contains("PostgreSQL", StringComparison.Ordinal) == true;
    }
}
