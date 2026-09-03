using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Invitations;

/// <summary>
/// A role offered by one invitation, carrying its tenant so the association can only ever be made inside a single
/// tenant. Only <see cref="Invitation"/> creates one, and the composite foreign keys repeat the tenant so
/// PostgreSQL rejects a cross-tenant pair even if the aggregate is bypassed.
/// </summary>
public sealed class InvitationRole
{
    private InvitationRole() { }

    public TenantId TenantId { get; private set; }

    public InvitationId InvitationId { get; private set; }

    public RoleId RoleId { get; private set; }

    internal static InvitationRole Create(Tenant tenant, InvitationId invitationId, Role role)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(role);
        if (role.TenantId != tenant.Id)
        {
            throw new InvalidOperationException("Invitations can only offer roles of their own tenant.");
        }

        // Offering a role grants nothing yet, so the tenant's authorization version deliberately does not move:
        // membership is what changes authority, and bumping here would make every invitation contend for the
        // tenant row.
        return new InvitationRole { TenantId = tenant.Id, InvitationId = invitationId, RoleId = role.Id };
    }
}
