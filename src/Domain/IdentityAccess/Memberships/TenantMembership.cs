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

    /// <summary>Lifts a suspension. It is the reverse of `Suspend` and of nothing else (IA-REQ-053).</summary>
    public void Reactivate(Tenant tenant)
    {
        EnsureTenant(tenant);
        if (Status != MembershipStatus.Suspended)
        {
            throw new InvalidOperationException("Only suspended memberships can be reactivated.");
        }

        Status = MembershipStatus.Active;
        tenant.IncrementAuthorizationVersion();
    }

    /// <summary>
    /// Ends the membership. It is where an active or suspended member stops being one, and there is deliberately
    /// no way from here back to `Active`: a person who was removed returns because somebody invited them again,
    /// which is a different act by a different person.
    /// </summary>
    public void Revoke(Tenant tenant)
    {
        EnsureTenant(tenant);
        if (Status is not (MembershipStatus.Active or MembershipStatus.Suspended))
        {
            throw new InvalidOperationException("Only active or suspended memberships can be revoked.");
        }

        Status = MembershipStatus.Revoked;
        tenant.IncrementAuthorizationVersion();
    }

    /// <summary>
    /// Returns a revoked membership to the invitation it came from, as pending rather than as active. What made
    /// it revoked was a decision; what makes it a member again is the acceptance that follows, and treating those
    /// as one step would let a removal be undone by whoever did it.
    /// </summary>
    public void Reinstate(Tenant tenant)
    {
        EnsureTenant(tenant);
        if (Status != MembershipStatus.Revoked)
        {
            throw new InvalidOperationException("Only revoked memberships can be reinstated.");
        }

        Status = MembershipStatus.PendingConfirmation;
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
