using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class RegistrationInitialRoleProvisioner(ApplicationDbContext context) : IRegistrationInitialRoleProvisioner
{
    public void AssignResponsibleOwner(Tenant tenant, TenantMembership membership)
    {
        var ownerRole = Role.CreateSystem(tenant, "Owner");
        var ownerAssignment = MembershipRole.Create(tenant, membership, ownerRole);

        context.TenantRoles.Add(ownerRole);
        context.MembershipRoles.Add(ownerAssignment);
    }
}
