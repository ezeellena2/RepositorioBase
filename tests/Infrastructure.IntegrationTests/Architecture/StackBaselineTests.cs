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
    public void Active_projects_do_not_reference_unselected_database_providers(string relativeProjectPath)
    {
        var project = XDocument.Load(GetRepositoryPath(relativeProjectPath));
        var packageReferences = project.Descendants("PackageReference").ToList();

        foreach (var packageReference in packageReferences.Where(IsDatabaseProviderPackage))
        {
            packageReference.Attribute("Condition")?.Value.ShouldContain("DatabaseProvider");
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
    public void Template_isolates_postgresql_migrations_to_the_postgresql_choice()
    {
        using var template = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(".template.config/template.json")));
        var source = template.RootElement.GetProperty("sources")[0];
        var exclusions = source.GetProperty("exclude").EnumerateArray().Select(element => element.GetString()).ToList();

        exclusions.ShouldContain("src/Infrastructure/Data/Migrations/**");

        var modifiers = source.GetProperty("modifiers").EnumerateArray().ToList();
        var postgresqlModifier = modifiers.Single(modifier => modifier.GetProperty("condition").GetString() == "(UsePostgreSQL)");
        postgresqlModifier.GetProperty("include").EnumerateArray().Select(element => element.GetString())
            .ShouldContain("src/Infrastructure/Data/Migrations/**");

        File.ReadAllText(GetRepositoryPath("CleanArchitecture.nuspec")).ShouldContain("<file src=\"build\\*.props\" target=\"content\\build\" />");
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
            packageId?.Contains("PostgreSQL", StringComparison.Ordinal) == true ||
            packageId?.Contains("SqlServer", StringComparison.Ordinal) == true ||
            packageId?.Contains("SQLite", StringComparison.Ordinal) == true;
    }
}
