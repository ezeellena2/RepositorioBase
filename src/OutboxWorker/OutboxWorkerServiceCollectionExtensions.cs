using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanArchitecture.OutboxWorker;

public static class OutboxWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxWorkerRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OutboxWorkerOptions>()
            .Bind(configuration.GetSection(OutboxWorkerOptions.SectionName))
            .Validate(options => options.IdleInterval > TimeSpan.Zero,
                "OutboxWorker:IdleInterval must be positive.")
            .Validate(options => options.MaximumFailureBackoff >= options.IdleInterval,
                "OutboxWorker:MaximumFailureBackoff must not be shorter than IdleInterval.")
            .Validate(options => options.UnhealthyAfterIntervals > 0,
                "OutboxWorker:UnhealthyAfterIntervals must be positive.")
            .ValidateOnStart();
        services.AddSingleton<OutboxWorkerHeartbeat>();
        services.AddSingleton<OutboxWorkerRuntime>();

        if (configuration.GetValue<bool>("IdentityAccess:Email:Enabled"))
        {
            services.AddHealthChecks()
                .AddCheck<OutboxWorkerHealthCheck>(
                    "outbox-worker-heartbeat",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["ready"]);
        }

        return services;
    }
}
