using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class PlatformAdminInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly VersionedTokenHash Hash = VersionedTokenHash.Of("platform-invitation-token");
    private static readonly VersionedTokenHash RotatedHash = VersionedTokenHash.Of("platform-invitation-rotated");

    private static Tenant Platform()
    {
        var tenant = Tenant.CreatePlatform();
        tenant.Activate();
        return tenant;
    }

    [Test]
    public void The_platform_tenant_is_created_with_its_own_reserved_slug()
    {
        var tenant = Tenant.CreatePlatform();

        tenant.Type.ShouldBe(TenantType.Platform);
        tenant.Status.ShouldBe(TenantStatus.PendingConfirmation);
        tenant.Slug.ShouldBe(TenantSlug.Platform);
    }

    /// <summary>Platform is the one tenant the system operates itself; suspending or closing it would lock everyone out.</summary>
    [Test]
    public void The_platform_tenant_cannot_be_suspended_or_closed()
    {
        var tenant = Platform();

        Should.Throw<InvalidOperationException>(tenant.Suspend);
        Should.Throw<InvalidOperationException>(tenant.Close);
        tenant.Status.ShouldBe(TenantStatus.Active);
    }

    [Test]
    public void Issue_binds_the_invitation_to_the_platform_tenant_and_starts_pending_and_undelivered()
    {
        var tenant = Platform();

        var invitation = PlatformAdminInvitation.Issue(tenant, "Owner@Example.Test", Hash, true, Now, Now.AddDays(7));

        invitation.Id.IsEmpty.ShouldBeFalse();
        invitation.TenantId.ShouldBe(tenant.Id);
        invitation.NormalizedEmail.ShouldBe("owner@example.test");
        invitation.TokenHash.ShouldBe(Hash);
        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        invitation.Delivery.ShouldBe(PlatformAdminInvitationDelivery.Pending);
        invitation.IsOwner.ShouldBeTrue();
        invitation.BoundIdentityId.ShouldBeNull();
        invitation.IsPendingAt(Now).ShouldBeTrue();
    }

    [Test]
    public void Only_the_platform_tenant_can_invite_administrators()
    {
        var organization = Tenant.CreateOrganization(TenantSlug.From("acme"));
        organization.Activate();

        Should.Throw<InvalidOperationException>(() =>
            PlatformAdminInvitation.Issue(organization, "owner@example.test", Hash, true, Now, Now.AddDays(7)));
    }

    [Test]
    public void An_invitation_cannot_be_issued_without_a_token_hash()
    {
        var tenant = Platform();

        Should.Throw<ArgumentException>(() =>
            PlatformAdminInvitation.Issue(tenant, "owner@example.test", default, true, Now, Now.AddDays(7)));
    }

    /// <summary>
    /// Recovery exists for an offer that cannot arrive. One that is merely in flight must not be rotated: doing so
    /// would invalidate a token the recipient may be about to use (IA-REQ-040).
    /// </summary>
    [Test]
    public void Only_an_expired_or_permanently_failed_pending_invitation_is_recoverable()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));

        invitation.IsRecoverableAt(Now).ShouldBeFalse("delivery is still in flight.");
        invitation.IsRecoverableAt(Now.AddDays(8)).ShouldBeTrue("the window has closed.");

        invitation.RecordDelivery(PlatformAdminInvitationDelivery.Delivered, Guid.NewGuid(), Now);
        invitation.IsRecoverableAt(Now).ShouldBeFalse("a delivered invitation is not a failed one.");

        invitation.RecordDelivery(PlatformAdminInvitationDelivery.PermanentlyFailed, null, Now);
        invitation.IsRecoverableAt(Now).ShouldBeTrue();
        invitation.DeliverySettledAt.ShouldBe(Now);
    }

    [Test]
    public void Reissuing_rotates_the_token_extends_the_window_and_puts_delivery_back_in_flight()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));
        invitation.RecordDelivery(PlatformAdminInvitationDelivery.PermanentlyFailed, Guid.NewGuid(), Now);

        invitation.Reissue(RotatedHash, Now.AddDays(8), Now.AddDays(15));

        invitation.TokenHash.ShouldBe(RotatedHash);
        invitation.ExpiresAt.ShouldBe(Now.AddDays(15));
        invitation.Delivery.ShouldBe(PlatformAdminInvitationDelivery.Pending);
        invitation.DeliveryMessageId.ShouldBeNull();
        invitation.IsRecoverableAt(Now.AddDays(8)).ShouldBeFalse("the replacement has not been sent yet.");
    }

    [Test]
    public void Reissuing_must_rotate_the_token()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));

        Should.Throw<ArgumentException>(() => invitation.Reissue(Hash, Now.AddDays(8), Now.AddDays(15)));
    }

    /// <summary>A token holder must not be able to move a standing Platform offer onto a different account.</summary>
    [Test]
    public void An_invitation_binds_to_one_identity_only()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));
        var identityId = Guid.NewGuid();

        invitation.Bind(identityId, Now);
        invitation.Bind(identityId, Now);

        invitation.BoundIdentityId.ShouldBe(identityId);
        invitation.BoundAt.ShouldBe(Now);
        Should.Throw<InvalidOperationException>(() => invitation.Bind(Guid.NewGuid(), Now));
    }

    [Test]
    public void Accepting_is_idempotent_for_the_bound_identity_and_refused_for_any_other()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));
        var identityId = Guid.NewGuid();

        invitation.Accept(identityId, Now);
        invitation.Accept(identityId, Now.AddMinutes(1));

        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Accepted);
        invitation.AcceptedAt.ShouldBe(Now);
        invitation.BoundIdentityId.ShouldBe(identityId);
        Should.Throw<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now));
    }

    [Test]
    public void An_expired_invitation_cannot_be_accepted_and_an_accepted_one_is_no_longer_recoverable()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));

        Should.Throw<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddDays(8)));

        invitation.Accept(Guid.NewGuid(), Now);
        invitation.IsRecoverableAt(Now.AddDays(8)).ShouldBeFalse("first owner activation permanently closes bootstrap.");
    }

    [Test]
    public void A_recipient_is_recognised_in_its_canonical_form_only()
    {
        var invitation = PlatformAdminInvitation.Issue(Platform(), "owner@example.test", Hash, true, Now, Now.AddDays(7));

        invitation.IsAddressedTo("Owner@Example.Test").ShouldBeTrue();
        invitation.IsAddressedTo(" owner@example.test ").ShouldBeTrue();
        invitation.IsAddressedTo("someone@example.test").ShouldBeFalse();
        invitation.IsAddressedTo(null).ShouldBeFalse();
        invitation.IsAddressedTo("not-an-address").ShouldBeFalse();
    }
}
