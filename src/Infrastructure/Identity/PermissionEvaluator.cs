using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Identity;

public sealed class PermissionEvaluator(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor) : IPermissionEvaluator
{
    public Task<bool> HasPermissionAsync(Guid identityId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var hasMatchingIdentity = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == identityId.ToString();
        return Task.FromResult(
            identityId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(permissionCode) &&
            hasMatchingIdentity &&
            Permissions.ApplicationScopedCodes.Contains(permissionCode) &&
            principal!.HasClaim(Permissions.ApplicationPermissionClaimType, permissionCode));
    }

    public async Task<bool> HasPermissionAsync(Guid identityId, TenantId tenantId, string permissionCode, CancellationToken cancellationToken = default)
    {
        if (identityId == Guid.Empty || tenantId.IsEmpty || string.IsNullOrWhiteSpace(permissionCode) || !Permissions.Catalog.Any(permission => permission.Code == permissionCode))
        {
            return false;
        }

        return await (
            from membership in context.TenantMemberships.AsNoTracking()
            join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            join membershipRole in context.MembershipRoles.AsNoTracking() on new { membership.TenantId, MembershipId = membership.Id } equals new { membershipRole.TenantId, membershipRole.MembershipId }
            join role in context.TenantRoles.AsNoTracking() on new { membershipRole.TenantId, membershipRole.RoleId } equals new { role.TenantId, RoleId = role.Id }
            join rolePermission in context.RolePermissions.AsNoTracking() on new { membershipRole.TenantId, membershipRole.RoleId } equals new { rolePermission.TenantId, rolePermission.RoleId }
            where membership.IdentityId == identityId
                && membership.TenantId == tenantId
                && membership.Status == MembershipStatus.Active
                && tenant.Status == TenantStatus.Active
                && !role.IsRetired
                && rolePermission.PermissionCode == permissionCode
            select rolePermission).AnyAsync(cancellationToken);
    }
}
