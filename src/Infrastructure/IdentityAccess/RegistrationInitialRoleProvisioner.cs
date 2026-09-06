using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class RegistrationInitialRoleProvisioner(ApplicationDbContext context) : IRegistrationInitialRoleProvisioner
{
    public async Task AssignResponsibleOwnerAsync(Tenant tenant, TenantMembership membership, CancellationToken cancellationToken)
    {
        var ownerRole = Role.CreateSystem(tenant, "Owner");
        var ownerAssignment = MembershipRole.Create(tenant, membership, ownerRole);

        context.TenantRoles.Add(ownerRole);
        context.MembershipRoles.Add(ownerAssignment);

        // Read from the persisted catalogue rather than from the code list, because `RolePermission.Create` takes
        // the entity and because a code the synchronizer has not written yet is a code no evaluator would honour.
        var permissions = await context.Permissions
            .Where(permission => Permissions.OrganizationOwnerCodes.Contains(permission.Code))
            .ToListAsync(cancellationToken);

        foreach (var permission in permissions)
        {
            context.RolePermissions.Add(RolePermission.Create(tenant, ownerRole, permission));
        }
    }
}
