using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Reads the offerable roles of one tenant and decides IA-REQ-047 in the same query pass.
/// <para>
/// The roles are tracked, because the invitation aggregate is built from them and the assignment rows it creates
/// carry the tenant of the role instance it was given. Retired roles are excluded here rather than after the
/// fact: they grant nothing to a member, so offering one would promise a place that confers nothing.
/// </para>
/// </summary>
public sealed class OfferableRoleReader(ApplicationDbContext context, IEffectivePermissionReader effectivePermissions) : IOfferableRoleReader
{
    public async Task<OfferableRoles> ResolveAsync(TenantId tenantId, Guid inviterId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (tenantId.IsEmpty || inviterId == Guid.Empty || roleIds.Count == 0)
        {
            return OfferableRoles.None;
        }

        var identifiers = roleIds.Select(RoleId.From).ToArray();
        var roles = await context.TenantRoles
            .Where(role => role.TenantId == tenantId && identifiers.Contains(role.Id) && !role.IsRetired)
            .ToListAsync(cancellationToken);

        // A role from another tenant, a retired one and one that never existed are indistinguishable on purpose:
        // saying which would disclose another organization's catalog to a caller who has no standing in it.
        if (roles.Count != identifiers.Length)
        {
            return OfferableRoles.None;
        }

        var offered = await context.RolePermissions
            .Where(grant => grant.TenantId == tenantId && identifiers.Contains(grant.RoleId))
            .Select(grant => grant.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        var held = await effectivePermissions.GetEffectivePermissionsAsync(inviterId, tenantId, cancellationToken);
        return new OfferableRoles(roles, offered.All(held.Contains));
    }
}

/// <summary>Writes the assignments an accepted invitation grants, on the same side of the boundary as the read.</summary>
public sealed class InvitationRoleAssigner(ApplicationDbContext context) : IInvitationRoleAssigner
{
    public async Task AssignAsync(Tenant tenant, TenantMembership membership, IReadOnlyCollection<Role> roles, CancellationToken cancellationToken)
    {
        var offered = roles.Select(role => role.Id).ToHashSet();
        var existing = await context.MembershipRoles
            .Where(link => link.TenantId == tenant.Id && link.MembershipId == membership.Id)
            .ToListAsync(cancellationToken);

        context.MembershipRoles.RemoveRange(existing.Where(link => !offered.Contains(link.RoleId)));
        var retained = existing.Select(link => link.RoleId).ToHashSet();
        foreach (var role in roles.Where(role => !retained.Contains(role.Id)))
        {
            context.MembershipRoles.Add(MembershipRole.Create(tenant, membership, role));
        }
    }
}
