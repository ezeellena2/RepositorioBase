using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

public interface IPermissionEvaluator
{
    Task<bool> HasPermissionAsync(Guid identityId, TenantId tenantId, string permissionCode, CancellationToken cancellationToken = default);
}
