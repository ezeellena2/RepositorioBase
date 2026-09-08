using System.Diagnostics.Metrics;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Infrastructure.IdentityAccess.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;

/// <summary>
/// The real metrics over a meter factory of this suite's own. Counting is not a behaviour any test here is
/// about, but it is a dependency of things that are, and a double that swallowed the calls would let a metric
/// carrying a key through unnoticed — which is exactly what <c>IdentityAccessMetricsTests</c> watches for.
/// </summary>
internal static class TestMetrics
{
    internal static IdentityAccessMetrics Instance { get; } = Create();

    internal static IdentityAccessMetrics Create() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>(),
            new NotRecovering());

    private sealed class NotRecovering : IRecoveryAdmission
    {
        public RecoveryAdmission Current => RecoveryAdmission.NotRecovering;
    }
}
