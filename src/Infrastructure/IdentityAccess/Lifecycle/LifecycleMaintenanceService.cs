using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// Runs retention maintenance and the attempt-budget sweep on a fixed cadence (IA-REQ-056, IA-REQ-057).
/// <para>
/// It is internal by construction: no route, no permission, no caller that is a person. An operator's only
/// powers over erasure are to read what the policy says and to stop it with a hold — ordering one is not among
/// them, and there is nothing here for a request to reach.
/// </para>
/// <para>
/// The loop is deliberately thin. Everything that decides anything lives in the cycle, which a test drives
/// directly with its own clock and its own bounds; a background loop that had to be waited on would make every
/// retention test a race with a timer.
/// </para>
/// </summary>
public sealed class LifecycleMaintenanceService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LifecycleMaintenanceService> logger) : BackgroundService
{
    /// <summary>How often a run starts. A **product default** accepted with C6.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var report = await scope.ServiceProvider.GetRequiredService<RetentionMaintenanceCycle>().RunOnceAsync(stoppingToken);

                // Only a run that did something is worth a line. A worker announcing every quarter-hour of
                // finding nothing is a worker nobody reads.
                if (!report.DidNothing) logger.LogInformation("Retention maintenance erased {Erased} rows.", report.Erased);

                // Closed attempt windows decide nothing and are the one thing anybody who can reach a bounded
                // route can make more of. A count only: the rows hold digests, and even those are not for a log.
                var swept = await scope.ServiceProvider
                    .GetRequiredService<Security.AttemptBudgetCleanup>().RunOnceAsync(stoppingToken);
                if (swept > 0) logger.LogInformation("Swept {Swept} closed attempt-budget windows.", swept);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                // Only the type. What is in scope here is personal data, and a log is not the place for it.
                logger.LogError("A retention maintenance run failed ({Failure}).", failure.GetType().Name);
            }

            await Task.Delay(Interval, timeProvider, stoppingToken);
        }
    }
}
