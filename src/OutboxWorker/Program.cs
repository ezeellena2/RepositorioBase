using CleanArchitecture.Infrastructure;
using CleanArchitecture.OutboxWorker;
using CleanArchitecture.Infrastructure.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOutboxWorkerServices();
builder.Services.AddOutboxWorkerRuntime(builder.Configuration);
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

// Retention maintenance runs here rather than in the web application, for the reason the dispatcher does: a
// background loop registered there would also start inside every functional test that boots the application, and
// this one deletes rows. It is unconditional — retention is not switched on by the mail setting, and a
// deployment with no policy already does nothing.
builder.Services.AddHostedService<CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle.LifecycleMaintenanceService>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();

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
