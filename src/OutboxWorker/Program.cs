using CleanArchitecture.Infrastructure;
using CleanArchitecture.OutboxWorker;
using CleanArchitecture.Infrastructure.Outbox;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddOutboxWorkerServices();
// A worker that is not delivering says so once, at start. Silence is the worst possible answer here: the
// symptom of a worker that never dispatched is a message nobody received, which looks exactly like a broken
// application rather than like a switch that was never turned on.
if (builder.Configuration.GetValue<bool>("IdentityAccess:Email:Enabled"))
{
    builder.Services.AddHostedService<Worker>();
}
else
{
    builder.Services.AddHostedService<IdleAnnouncement>();
}

builder.Build().Run();

/// <summary>Says, once, that delivery is switched off — so an idle worker is never a silent one.</summary>
internal sealed class IdleAnnouncement(ILogger<IdleAnnouncement> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("Outbox delivery is disabled (IdentityAccess:Email:Enabled is not true); no message will be delivered.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
