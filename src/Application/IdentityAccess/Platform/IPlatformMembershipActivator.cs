using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// Grants a Platform membership the system role the bootstrap ceremony created.
/// <para>
/// It is a port for the same reason the invitation slice has one: the application context deliberately does not
/// expose the tenant authorization tables, so that no handler can query or rewrite authority in bulk. A named
/// operation that can only attach one existing role to one membership is a much smaller surface than a DbSet.
/// </para>
/// </summary>
public interface IPlatformMembershipActivator
{
    /// <summary>
    /// Attaches the named active system role. It answers false when no such role exists, which is the case a
    /// caller must not paper over by creating one: authority always predates the grant (IA-REQ-042).
    /// </summary>
    Task<bool> TryAssignSystemRoleAsync(Tenant platform, TenantMembership membership, string roleName, CancellationToken cancellationToken);
}
