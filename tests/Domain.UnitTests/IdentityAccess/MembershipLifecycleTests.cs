using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

/// <summary>
/// What an `Organization` may do to one of its memberships, and what it may not (IA-REQ-053).
/// <para>
/// The shape that matters is which transitions exist at all. `Suspended` is a pause and reverses; `Revoked` is
/// the end of that membership and reverses only through a fresh invitation, which is a different act by a
/// different person — so there is no method here that turns it back into an active member.
/// </para>
/// </summary>
public sealed class MembershipLifecycleTests
{
    [Test]
    public void A_suspended_membership_can_be_reactivated_and_holds_what_it_held()
    {
        var (tenant, membership) = ActiveMember();
        membership.Suspend(tenant);
        var before = tenant.AuthorizationVersion;

        membership.Reactivate(tenant);

        membership.Status.ShouldBe(MembershipStatus.Active);
        tenant.AuthorizationVersion.ShouldBeGreaterThan(before,
            "the evaluator has to see the change on the next request, and the version is how it knows");
    }

    [Test]
    public void Only_a_suspended_membership_can_be_reactivated()
    {
        var (tenant, membership) = ActiveMember();

        Should.Throw<InvalidOperationException>(() => membership.Reactivate(tenant));
        membership.Status.ShouldBe(MembershipStatus.Active);
    }

    [TestCase(MembershipStatus.Active)]
    [TestCase(MembershipStatus.Suspended)]
    public void An_active_or_suspended_membership_can_be_revoked(MembershipStatus from)
    {
        var (tenant, membership) = ActiveMember();
        if (from == MembershipStatus.Suspended) membership.Suspend(tenant);
        var before = tenant.AuthorizationVersion;

        membership.Revoke(tenant);

        membership.Status.ShouldBe(MembershipStatus.Revoked);
        tenant.AuthorizationVersion.ShouldBeGreaterThan(before);
    }

    /// <summary>
    /// The one that shapes the whole feature. A revoked membership does not come back by being reactivated: it
    /// comes back through a fresh invitation somebody issues, which is why `Reinstate` takes the invitation's own
    /// act as its starting point and why reactivation refuses it outright.
    /// </summary>
    [Test]
    public void A_revoked_membership_is_not_reactivated_back_into_the_organization()
    {
        var (tenant, membership) = ActiveMember();
        membership.Revoke(tenant);

        Should.Throw<InvalidOperationException>(() => membership.Reactivate(tenant));
        Should.Throw<InvalidOperationException>(() => membership.Suspend(tenant));
        membership.Status.ShouldBe(MembershipStatus.Revoked);
    }

    [Test]
    public void Reinstating_a_revoked_membership_returns_it_as_pending_rather_than_as_active()
    {
        var (tenant, membership) = ActiveMember();
        membership.Revoke(tenant);

        membership.Reinstate(tenant);

        membership.Status.ShouldBe(MembershipStatus.PendingConfirmation,
            "a returning member confirms again; reinstating is not the same as never having left");
    }

    [Test]
    public void Only_a_revoked_membership_can_be_reinstated()
    {
        var (tenant, membership) = ActiveMember();

        Should.Throw<InvalidOperationException>(() => membership.Reinstate(tenant));
    }

    [Test]
    public void No_transition_reaches_across_tenants()
    {
        var (tenant, membership) = ActiveMember();
        var elsewhere = Organization();

        Should.Throw<InvalidOperationException>(() => membership.Suspend(elsewhere));
        Should.Throw<InvalidOperationException>(() => membership.Revoke(elsewhere));
        Should.Throw<InvalidOperationException>(() => membership.Reactivate(elsewhere));
        Should.Throw<InvalidOperationException>(() => membership.Reinstate(elsewhere));
        membership.Status.ShouldBe(MembershipStatus.Active);
    }

    private static (Tenant Tenant, TenantMembership Membership) ActiveMember()
    {
        var tenant = Organization();
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        membership.Activate(tenant);
        return (tenant, membership);
    }

    private static Tenant Organization()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"members-{Guid.NewGuid():N}"));
        tenant.Activate();
        return tenant;
    }
}

/// <summary>
/// The one explicit owner an `Organization` has, and how it moves (IA-REQ-053).
/// <para>
/// Ownership is a single reference on the tenant rather than a flag on a membership, because "exactly one" is a
/// statement about the tenant and a flag would let two rows both claim it. The transfer is one act: the previous
/// owner stops being one at the same instant the next one starts.
/// </para>
/// </summary>
public sealed class TenantOwnershipTests
{
    [Test]
    public void A_new_organization_has_no_owner_until_one_is_named()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"owner-{Guid.NewGuid():N}"));

        tenant.OwnerMembershipId.ShouldBeNull("registration names it; the type does not invent one");
    }

    [Test]
    public void Naming_an_owner_records_it_and_moves_the_authorization_version()
    {
        var (tenant, membership) = ActiveMember();
        var before = tenant.AuthorizationVersion;

        tenant.TransferOwnershipTo(membership);

        tenant.OwnerMembershipId.ShouldBe(membership.Id);
        tenant.AuthorizationVersion.ShouldBeGreaterThan(before);
    }

    [Test]
    public void Ownership_moves_to_another_active_member_of_the_same_organization()
    {
        var (tenant, first) = ActiveMember();
        var second = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        second.Activate(tenant);
        tenant.TransferOwnershipTo(first);

        tenant.TransferOwnershipTo(second);

        tenant.OwnerMembershipId.ShouldBe(second.Id, "there is one owner, so naming the next one unnames the last");
    }

    [Test]
    public void Ownership_cannot_be_given_to_a_membership_of_another_organization()
    {
        var (tenant, _) = ActiveMember();
        var elsewhere = Tenant.CreateOrganization(TenantSlug.From($"other-{Guid.NewGuid():N}"));
        elsewhere.Activate();
        var stranger = TenantMembership.CreateResponsible(elsewhere, Guid.NewGuid());
        stranger.Activate(elsewhere);

        Should.Throw<InvalidOperationException>(() => tenant.TransferOwnershipTo(stranger));
        tenant.OwnerMembershipId.ShouldBeNull();
    }

    [TestCase(MembershipStatus.PendingConfirmation)]
    [TestCase(MembershipStatus.Suspended)]
    [TestCase(MembershipStatus.Revoked)]
    public void Ownership_cannot_be_given_to_a_membership_that_is_not_active(MembershipStatus status)
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"owner-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        if (status != MembershipStatus.PendingConfirmation) membership.Activate(tenant);
        if (status == MembershipStatus.Suspended) membership.Suspend(tenant);
        if (status == MembershipStatus.Revoked) membership.Revoke(tenant);

        Should.Throw<InvalidOperationException>(() => tenant.TransferOwnershipTo(membership));
        tenant.OwnerMembershipId.ShouldBeNull();
    }

    /// <summary>
    /// Only an `Organization` has delegated administration to own. `Personal` is one person's own context and
    /// `Platform` is the deployment's; naming an owner on either would be inventing a role neither has.
    /// </summary>
    [Test]
    public void Only_an_organization_has_an_owner()
    {
        var personal = Tenant.CreatePersonal(TenantSlug.From($"personal-{Guid.NewGuid():N}"));
        personal.Activate();
        var membership = TenantMembership.CreateResponsible(personal, Guid.NewGuid());
        membership.Activate(personal);

        Should.Throw<InvalidOperationException>(() => personal.TransferOwnershipTo(membership));
    }

    private static (Tenant Tenant, TenantMembership Membership) ActiveMember()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"owner-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, Guid.NewGuid());
        membership.Activate(tenant);
        return (tenant, membership);
    }
}
