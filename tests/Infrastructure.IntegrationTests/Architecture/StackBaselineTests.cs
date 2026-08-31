using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
