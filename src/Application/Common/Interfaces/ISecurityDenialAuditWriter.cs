using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface ISecurityDenialAuditWriter
{
    Task WriteDeniedAsync(SecurityDenialAudit audit, CancellationToken cancellationToken = default);
}

/// <summary>Strictly allowlisted evidence for a sensitive authorization denial.</summary>
public sealed record SecurityDenialAudit(
    string CorrelationId,
    Guid? ActorId,
    TenantId? TenantId,
    string PermissionCode,
    string Outcome,
    Guid? SessionId = null);
