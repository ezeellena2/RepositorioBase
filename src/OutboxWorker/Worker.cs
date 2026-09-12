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
public sealed class Worker(IServiceScopeFactory scopeFactory, OutboxWorkerRuntime runtime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        runtime.RunAsync(async cancellationToken =>
        {
            using var scope = scopeFactory.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<OutboxDispatcher>()
                .DispatchDueAsync(cancellationToken);
        }, stoppingToken);
}
