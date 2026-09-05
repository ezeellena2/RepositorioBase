using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Web.HostedServices;

/// <summary>
/// Drains the outbox from inside the web application, and only when messages are being written to a local drop
/// folder.
/// <para>
/// The loop normally belongs to its own process, for a good reason: a poller registered here would also start
/// inside every functional test that boots the application and would race the rows those tests assert on. That
/// reason still holds, and this does not weaken it — a local drop is permitted only on an explicit Development,
/// Test or Testing host, and no functional test configures one.
/// </para>
/// <para>
/// What it buys is that one process seals the tokens and opens them. Delivering locally from a second process
/// means sharing a Data Protection key ring and discriminator between the two, and getting that wrong produces
/// exactly nothing — every envelope unreadable, every message failing closed, and no message anywhere. For a
/// developer trying to follow their own invitation link, that is a bad trade.
/// </para>
/// </summary>
public sealed class LocalOutboxDeliveryService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityEmailOptions> options,
    TimeProvider timeProvider,
    ILogger<LocalOutboxDeliveryService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DeliversLocally) return;

        logger.LogInformation("Delivering identity mail to the local drop folder; nothing is sent.");

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
                // Only the type: the one interesting value in scope is a token, and a log is not the place for it.
                logger.LogError("A local outbox dispatch pass failed ({Failure}).", exception.GetType().Name);
            }

            if (delivered == 0)
            {
                await Task.Delay(IdleInterval, timeProvider, stoppingToken);
            }
        }
    }
}
