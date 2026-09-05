using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting.Server;

namespace CleanArchitecture.Infrastructure.Data;

internal sealed class DatabaseMigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var server = scope.ServiceProvider.GetService<IServer>();
        if (!DatabaseMigrationExecutionPolicy.ShouldMigrate(server?.GetType().FullName))
        {
            return;
        }

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
