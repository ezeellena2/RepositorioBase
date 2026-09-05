using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess.Platform;

/// <summary>
/// What bootstrap recovery is allowed to act on (IA-REQ-040). Every case that must not be recovered is here,
/// because the value of this decision is entirely in what it refuses — the one case it permits is the easy half.
/// </summary>
public sealed class RecoverPendingPlatformOwnerInvitationValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private const string ConfiguredEmail = "owner@example.test";

    private readonly RecoverPendingPlatformOwnerInvitationValidator _validator = new();

    private static PlatformAdminInvitation Invitation(bool isOwner = true, string email = ConfiguredEmail)
    {
        var platform = Tenant.CreatePlatform();
        platform.Activate();
        return PlatformAdminInvitation.Issue(platform, email, VersionedTokenHash.Of(Guid.NewGuid().ToString("N")), isOwner, Now, Now.AddDays(7));
    }

    [Test]
    public void An_expired_pending_owner_invitation_is_recovered_to_its_own_recipient()
    {
        var decision = _validator.Decide(Invitation(), ConfiguredEmail, Now.AddDays(8));

        decision.IsEligible.ShouldBeTrue();
        decision.Recipient.ShouldBe(ConfiguredEmail);
    }

    [Test]
    public void A_permanently_failed_delivery_is_recovered_before_the_window_closes()
    {
        var invitation = Invitation();
        invitation.RecordDelivery(PlatformAdminInvitationDelivery.PermanentlyFailed, Guid.NewGuid(), Now);

        _validator.Decide(invitation, ConfiguredEmail, Now).IsEligible.ShouldBeTrue();
    }

    /// <summary>An offer still on its way must not be rotated: the recipient may be about to use that token.</summary>
    [Test]
    public void An_invitation_still_in_flight_is_not_recovered()
    {
        _validator.Decide(Invitation(), ConfiguredEmail, Now).IsEligible.ShouldBeFalse();
    }

    [Test]
    public void A_delivered_invitation_within_its_window_is_not_recovered()
    {
        var invitation = Invitation();
        invitation.RecordDelivery(PlatformAdminInvitationDelivery.Delivered, Guid.NewGuid(), Now);

        _validator.Decide(invitation, ConfiguredEmail, Now).IsEligible.ShouldBeFalse();
    }

    /// <summary>First owner activation permanently closes bootstrap.</summary>
    [Test]
    public void An_accepted_invitation_is_never_recovered()
    {
        var invitation = Invitation();
        invitation.Accept(Guid.NewGuid(), Now);

        _validator.Decide(invitation, ConfiguredEmail, Now.AddDays(8)).IsEligible.ShouldBeFalse();
    }

    [Test]
    public void A_cancelled_invitation_is_never_recovered()
    {
        var invitation = Invitation();
        invitation.Cancel(Now);

        _validator.Decide(invitation, ConfiguredEmail, Now.AddDays(8)).IsEligible.ShouldBeFalse();
    }

    /// <summary>Recovery is for the bootstrap owner. An administrator's offer is reissued by an owner, not by this.</summary>
    [Test]
    public void An_administrator_invitation_is_not_a_bootstrap_owner_invitation()
    {
        _validator.Decide(Invitation(isOwner: false), ConfiguredEmail, Now.AddDays(8)).IsEligible.ShouldBeFalse();
    }

    /// <summary>
    /// The load-bearing refusal: editing the configured address after bootstrap must not move the pending owner,
    /// which is what would turn this endpoint into a way to redirect the system's highest authority.
    /// </summary>
    [Test]
    public void A_configuration_change_does_not_move_the_pending_owner()
    {
        var decision = _validator.Decide(Invitation(), "someone.else@example.test", Now.AddDays(8));

        decision.IsEligible.ShouldBeFalse();
        decision.Recipient.ShouldBeNull();
    }

    [Test]
    public void The_configured_address_is_compared_in_its_canonical_form()
    {
        _validator.Decide(Invitation(), " Owner@Example.Test ", Now.AddDays(8)).IsEligible.ShouldBeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-address")]
    public void Without_usable_configuration_there_is_nothing_to_recover(string? configured)
    {
        _validator.Decide(Invitation(), configured, Now.AddDays(8)).IsEligible.ShouldBeFalse();
    }

    [Test]
    public void With_no_invitation_at_all_there_is_nothing_to_recover()
    {
        _validator.Decide(null, ConfiguredEmail, Now).IsEligible.ShouldBeFalse();
    }
}
