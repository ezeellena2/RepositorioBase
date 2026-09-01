using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.Auditing;

/// <summary>
/// Writes denial evidence through an isolated service scope so an enclosing
/// business transaction cannot roll it back. Only allowlisted machine values
/// are persisted.
/// </summary>
public sealed class SecurityDenialAuditWriter(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : ISecurityDenialAuditWriter
{
    public async Task WriteDeniedAsync(SecurityDenialAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.AuditEvents.Add(AuditEvent.CreateAuthorizationDenied(
            audit.TenantId,
            audit.ActorId,
            audit.SessionId,
            audit.CorrelationId,
            audit.PermissionCode,
            audit.Outcome,
            timeProvider.GetUtcNow()));

        using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await context.SaveChangesAsync(persistenceTimeout.Token);
    }
}
