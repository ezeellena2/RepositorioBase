using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// Moves a permission on or off a role so a test can set up an inviter who can — or can no longer — make a given
/// offer. IA-REQ-047 is a comparison between two permission sets, so a test that cannot change either of them
/// cannot discriminate an implementation of it.
/// </summary>
internal static class InvitationGrants
{
    internal static async Task GrantAsync(Guid roleId, string permissionCode)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identifier = RoleId.From(roleId);
        var role = await context.TenantRoles.SingleAsync(candidate => candidate.Id == identifier);
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == role.TenantId);
        var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == permissionCode);
        context.Add(RolePermission.Create(tenant, role, permission));
        await context.SaveChangesAsync();
    }

    internal static async Task RevokeAsync(Guid roleId, string permissionCode)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identifier = RoleId.From(roleId);
        var assignment = await context.RolePermissions.SingleAsync(
            candidate => candidate.RoleId == identifier && candidate.PermissionCode == permissionCode);
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == assignment.TenantId);
        assignment.Revoke(tenant);
        context.Remove(assignment);
        await context.SaveChangesAsync();
    }
}
