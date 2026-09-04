using CleanArchitecture.Infrastructure.Outbox;

namespace CleanArchitecture.OutboxWorker;

/// <summary>
/// The loop, and nothing else. Every rule about what to deliver, when to retry it and when to give up lives in
/// <see cref="OutboxDispatcher"/>, which runs one pass on demand — that is what makes those rules testable
/// against a controlled clock instead of against a background timer.
/// <para>
/// It runs as its own process rather than inside the web application. A poller registered there would also start
/// inside every functional test that boots the application, and would race the very rows those tests assert on.
/// </para>
/// </summary>
public sealed class Worker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// How long to wait after a pass that found nothing. A pass that delivered something tries again immediately,
    /// because a backlog is drained rather than metered.
    /// </summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = 0;
            try
            {
                using var scope = scopeFactory.CreateScope();
                delivered = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failed pass must not end the worker: the messages it did not reach are still due, and their
                // own attempt counters already govern how often anything is retried. The message identifiers are
                // deliberately absent here — this log line says the loop stumbled, not what it was carrying.
                logger.LogError(exception, "An outbox dispatch pass failed.");
            }

            if (delivered == 0)
            {
                await Task.Delay(IdleInterval, timeProvider, stoppingToken);
            }
        }
    }
}
