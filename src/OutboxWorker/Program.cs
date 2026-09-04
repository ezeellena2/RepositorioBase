using CleanArchitecture.Infrastructure;
using CleanArchitecture.OutboxWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();
builder.Services.AddHostedService<Worker>();

builder.Build().Run();
