using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.Outbox;

public static class WorkerRegistration
{
    public static void AddOutboxWorkerServices(this IHostApplicationBuilder builder)
    {
        builder.AddApplicationServices();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRouting();
        builder.Services.AddScoped<IUser, BackgroundUser>();
        builder.AddInfrastructureServices();
    }

    private sealed class BackgroundUser : IUser
    {
        public Guid? Id => null;
        public List<string>? Roles => null;
    }
}
