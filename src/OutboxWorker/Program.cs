using CleanArchitecture.Infrastructure;
using CleanArchitecture.OutboxWorker;
using CleanArchitecture.Infrastructure.Outbox;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddOutboxWorkerServices();
if (builder.Configuration.GetValue<bool>("IdentityAccess:Email:Enabled"))
    builder.Services.AddHostedService<Worker>();

builder.Build().Run();
