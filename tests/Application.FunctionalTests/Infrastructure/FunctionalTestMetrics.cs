using System.Diagnostics.Metrics;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Infrastructure.IdentityAccess.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// The real metrics for the few places a test constructs a collaborator itself. A double that swallowed the
/// calls would hide a label carrying a key, which is the one thing these instruments must never do.
/// </summary>
internal static class FunctionalTestMetrics
{
    internal static IdentityAccessMetrics Instance { get; } =
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>(),
            new NotRecovering());

    private sealed class NotRecovering : IRecoveryAdmission
    {
        public RecoveryAdmission Current => RecoveryAdmission.NotRecovering;
    }
}
