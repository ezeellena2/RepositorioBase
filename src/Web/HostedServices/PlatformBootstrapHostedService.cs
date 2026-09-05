using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Web.HostedServices;

/// <summary>
/// Runs the Platform bootstrap ceremony as the application starts (IA-REQ-040).
/// <para>
/// It runs here rather than behind a route because a route that created the Platform tenant would be a route that
/// created the system's highest authority from outside it. Running on every start is safe because the ceremony is
/// idempotent: it does nothing once the tenant exists, and nothing at all when no owner email is configured.
/// </para>
/// <para>
/// A failure never stops the host. A deployment whose bootstrap could not complete is one whose Platform is simply
/// absent — recoverable on the next start, or through the recovery endpoint — and refusing to serve the rest of
/// the application because of it would turn a missing administrator into an outage.
/// </para>
/// </summary>
public sealed class PlatformBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlatformBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<BootstrapPlatformOwner>();
            if (await bootstrapper.ExecuteAsync(new BootstrapPlatformOwnerCommand(), cancellationToken))
            {
                logger.LogInformation("Platform bootstrap created the singleton tenant and invited its owner.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately no detail beyond the type: the one interesting value in scope is a configured email,
            // and a startup log is not the place for it (IA-REQ-029).
            logger.LogError("Platform bootstrap did not complete: {Failure}.", exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
