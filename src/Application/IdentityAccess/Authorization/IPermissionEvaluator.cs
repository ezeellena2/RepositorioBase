using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

public interface IPermissionEvaluator
{
    /// <summary>
    /// Evaluates an application-scoped permission. Implementations without a validated
    /// global grant source must deny rather than infer authority from client input.
    /// </summary>
    Task<bool> HasPermissionAsync(Guid identityId, string permissionCode, CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(Guid identityId, TenantId tenantId, string permissionCode, CancellationToken cancellationToken = default);
}
