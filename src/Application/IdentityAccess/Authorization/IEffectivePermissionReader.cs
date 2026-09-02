using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

/// <summary>
/// Projects the permissions a single membership actually grants inside one tenant. It is the read counterpart of
/// <see cref="IPermissionEvaluator"/>: the persisted membership, tenant state and role assignments are the only
/// source, so no other tenant's grants and no cookie claim can ever enter the answer (IA-REQ-006..008).
/// </summary>
public interface IEffectivePermissionReader
{
    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid identityId, TenantId tenantId, CancellationToken cancellationToken = default);
}
