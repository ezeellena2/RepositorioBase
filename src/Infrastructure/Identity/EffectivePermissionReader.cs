using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Identity;

/// <summary>
/// Resolves effective permissions with the same predicate <see cref="PermissionEvaluator"/> uses for a single
/// code: one active membership of one active tenant, through its non-retired roles, and only codes the backend
/// still defines. Anything suspended, retired, retired from the catalog, or belonging to another tenant
/// contributes nothing, so the projection can never be wider than the decision the evaluator would make for the
/// same request.
/// </summary>
public sealed class EffectivePermissionReader(ApplicationDbContext context) : IEffectivePermissionReader
{
    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid identityId, TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (identityId == Guid.Empty || tenantId.IsEmpty)
        {
            return [];
        }

        var codes = await (
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
            select rolePermission.PermissionCode).Distinct().ToListAsync(cancellationToken);

        // The evaluator denies a code the catalog no longer defines, so the projection must drop it too. Without
        // this the context could advertise a permission every authorized request would then refuse.
        // A stable order keeps the identity-context contract deterministic for clients and contract tests.
        return codes
            .Where(code => Permissions.Catalog.Any(definition => definition.Code == code))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
