using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;

public static class TestServices
{
    private static IServiceScopeFactory? _scopeFactory;

    internal static void Configure(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public static IServiceScope CreateScope() =>
        (_scopeFactory ?? throw new InvalidOperationException("The integration test host has not started.")).CreateScope();
}
