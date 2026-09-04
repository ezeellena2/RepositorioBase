using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Invitations;

/// <summary>
/// Resolves the roles an invitation may offer, and answers whether the inviter is entitled to offer them.
/// <para>
/// It exists because tenant authorization never crosses <c>IApplicationDbContext</c>: role, permission and
/// assignment rows reach the Application layer only through a port that carries the invariant with them, so no
/// use case can assemble its own view of who may grant what. That is also why the entitlement question is
/// answered here rather than by handing the caller two permission sets to compare — the comparison IS the rule
/// (IA-REQ-047), and one implementation of it is one place for it to be wrong.
/// </para>
/// </summary>
public interface IOfferableRoleReader
{
    Task<OfferableRoles> ResolveAsync(TenantId tenantId, Guid inviterId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of resolving an offer. <paramref name="Roles"/> is empty unless every requested role exists, is
/// live and belongs to the tenant; <paramref name="WithinInviterAuthority"/> is false when any of them grants a
/// permission the inviter does not hold there.
/// </summary>
public sealed record OfferableRoles(IReadOnlyList<Role> Roles, bool WithinInviterAuthority)
{
    public static readonly OfferableRoles None = new([], false);

    /// <summary>An offer is usable only when it resolved completely and the inviter may make it.</summary>
    public bool IsOfferable => Roles.Count > 0 && WithinInviterAuthority;
}
