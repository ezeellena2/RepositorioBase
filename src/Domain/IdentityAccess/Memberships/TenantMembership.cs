using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Memberships;

public sealed class TenantMembership : BaseEntity<MembershipId>
{
    private TenantMembership()
    {
    }

    public TenantId TenantId { get; private set; }

    public Guid IdentityId { get; private set; }

    public MembershipStatus Status { get; private set; }

    public static TenantMembership CreateResponsible(Tenant tenant, Guid identityId)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        if (tenant.Id.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(tenant));
        }

        return new TenantMembership
        {
            Id = MembershipId.New(),
            TenantId = tenant.Id,
            IdentityId = identityId,
            Status = MembershipStatus.PendingConfirmation
        };
    }

    /// <summary>
    /// The membership an accepted invitation creates. It is the same shape as the responsible member's — pending
    /// until activated — but it is not the responsible member, and a factory whose name says otherwise would make
    /// every reader of the acceptance path check whether it meant it.
    /// </summary>
    public static TenantMembership CreateInvited(Tenant tenant, Guid identityId) => CreateResponsible(tenant, identityId);

    public void Activate(Tenant tenant)
    {
        EnsureTenant(tenant);
        if (tenant.Status != TenantStatus.Active || Status != MembershipStatus.PendingConfirmation)
        {
            throw new InvalidOperationException("Only pending memberships in active tenants can be activated.");
        }

        Status = MembershipStatus.Active;
        tenant.IncrementAuthorizationVersion();
    }

    public void Suspend(Tenant tenant)
    {
        EnsureTenant(tenant);
        if (Status != MembershipStatus.Active)
        {
            throw new InvalidOperationException("Only active memberships can be suspended.");
        }

        Status = MembershipStatus.Suspended;
        tenant.IncrementAuthorizationVersion();
    }

    private void EnsureTenant(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id != TenantId)
        {
            throw new InvalidOperationException("Membership does not belong to the supplied tenant.");
        }
    }
}
