using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// Inviting and revoking Platform administrators through the normal authority (IA-REQ-042).
/// <para>
/// The order these tests follow is the one the panel follows: list the directory, then act on a membership id it
/// returned. That is not decoration — the only identifier a mutation accepts is one the protected directory
/// already handed over, so a caller cannot act on a membership they were never shown.
/// </para>
/// </summary>
public sealed class PlatformAdministrationTests : TestBase
{
    [Test]
    public async Task An_owner_lists_the_directory_and_invites_an_administrator_without_elevating_anybody()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var directory = await TestApp.SendAsync(new ListPlatformAdministratorsQuery(new PlatformDirectoryQuery(25, null)));
        directory.IsSuccess.ShouldBeTrue();
        var listed = directory.Value!.Items.ShouldHaveSingleItem();
        listed.IdentityId.ShouldBe(owner.IdentityId);
        listed.IsOwner.ShouldBeTrue();
        listed.MembershipStatus.ShouldBe(nameof(MembershipStatus.Active));

        var invited = await TestApp.SendAsync(new InvitePlatformAdministratorCommand("second-admin@example.test"));

        invited.IsSuccess.ShouldBeTrue();
        var offers = await TestApp.ListAsync<PlatformAdminInvitation>();
        offers.Count.ShouldBe(2);
        var administrator = offers.Single(offer => !offer.IsOwner);
        administrator.NormalizedEmail.ShouldBe("second-admin@example.test");
        administrator.Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        administrator.BoundIdentityId.ShouldBeNull();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "an invitation is not a membership.");
    }

    /// <summary>
    /// A Platform change is not something a session can do because it once proved a factor. Without a recent
    /// step-up the request is refused before it reads anything (IA-REQ-041/043).
    /// </summary>
    [Test]
    public async Task Without_a_recent_step_up_an_administrator_change_is_refused()
    {
        await PlatformScenario.ActiveOwnerAsync();
        TestApp.SetSessionId(Guid.NewGuid()); // A second session, which never stepped up.

        var result = await TestApp.SendAsync(new InvitePlatformAdministratorCommand("second-admin@example.test"));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("recent_mfa_required");
        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(1);
    }

    /// <summary>Re-inviting the same address revives the standing offer in place rather than leaving two.</summary>
    [Test]
    public async Task Re_inviting_the_same_address_rotates_its_offer_instead_of_creating_a_second()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await TestApp.SendAsync(new InvitePlatformAdministratorCommand("second-admin@example.test"));
        var first = (await TestApp.ListAsync<PlatformAdminInvitation>()).Single(offer => !offer.IsOwner).TokenHash;

        await TestApp.SendAsync(new InvitePlatformAdministratorCommand("second-admin@example.test"));

        var offers = (await TestApp.ListAsync<PlatformAdminInvitation>()).Where(offer => !offer.IsOwner).ToArray();
        offers.Length.ShouldBe(1);
        offers[0].TokenHash.ShouldNotBe(first, "reissuing rotates the token.");
    }

    /// <summary>
    /// The load-bearing refusal of IA-REQ-042. A Platform with no owner is one nobody can ever administer again,
    /// and there is no higher authority to restore it from.
    /// </summary>
    [Test]
    public async Task The_last_active_owner_cannot_be_revoked()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single(item => item.IdentityId == owner.IdentityId);

        var result = await TestApp.SendAsync(new RevokePlatformAdministratorCommand(membership.Id.Value));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_platform_operation");
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.Active);
    }

    [Test]
    public async Task A_membership_that_is_not_a_platform_one_cannot_be_revoked_through_this_route()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var foreignIdentity = await IdentityHttpHarness.SeedConfirmedUserAsync($"member-{Guid.NewGuid():N}@example.test", PlatformScenario.ValidPassword);
        await IdentityHttpHarness.SeedActiveMembershipAsync(foreignIdentity);
        var foreign = (await TestApp.ListAsync<TenantMembership>()).Single(item => item.IdentityId == foreignIdentity);

        var result = await TestApp.SendAsync(new RevokePlatformAdministratorCommand(foreign.Id.Value));

        result.IsFailure.ShouldBeTrue();
        (await TestApp.ListAsync<TenantMembership>()).Single(item => item.Id == foreign.Id).Status.ShouldBe(MembershipStatus.Active);
        owner.IdentityId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task An_unknown_membership_is_refused_without_saying_whether_it_exists()
    {
        await PlatformScenario.ActiveOwnerAsync();

        var unknown = await TestApp.SendAsync(new RevokePlatformAdministratorCommand(Guid.NewGuid()));
        var empty = await TestApp.SendAsync(new RevokePlatformAdministratorCommand(Guid.Empty));

        unknown.Error!.Code.ShouldBe("invalid_platform_operation");
        empty.Error!.Code.ShouldBe(unknown.Error.Code);
    }

    /// <summary>
    /// A second administrator walks exactly the same gates, and gains nothing until the last one. This is the
    /// whole shape of IA-REQ-042 in one test: invited, registered, confirmed, enrolled, and only then a member.
    /// </summary>
    [Test]
    public async Task A_second_administrator_gains_nothing_until_the_same_gates_complete()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await TestApp.SendAsync(new InvitePlatformAdministratorCommand("second-admin@example.test"));
        var offer = (await TestApp.ListAsync<PlatformAdminInvitation>()).Single(item => !item.IsOwner);
        var token = await PlatformScenario.SealedTokenAsync(offer.DeliveryMessageId!.Value);

        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "registration grants nothing.");

        var confirmation = await PlatformScenario.SealedTokenAsync(
            (await PlatformScenario.MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.ConfirmPlatformInviteeCommand(token, confirmation));
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "confirmation grants nothing either.");

        var newcomer = (await TestApp.ListAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>())
            .Single(user => user.NormalizedEmail == "SECOND-ADMIN@EXAMPLE.TEST");
        PlatformScenario.RunAs(newcomer.Id);
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "enrolling grants nothing either.");

        await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(token, PlatformScenario.TotpCode(enrollment.Value!.SharedKey)));
        (await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(token))).IsSuccess.ShouldBeTrue();

        var memberships = await TestApp.ListAsync<TenantMembership>();
        memberships.Count.ShouldBe(2);
        memberships.Single(item => item.IdentityId == newcomer.Id).Status.ShouldBe(MembershipStatus.Active);
    }

    /// <summary>
    /// The rule protects the last owner, not every membership. An administrator holds no owner role, so revoking
    /// one is exactly the ordinary case the rule is not about.
    /// <para>
    /// Note what this does not show: there is no route that creates a second owner. An administrator invitation is
    /// never an owner offer, so the bootstrap owner is the only one there ever is, and it can never be revoked.
    /// That is a deliberate boundary of this increment rather than something the SPEC fixes.
    /// </para>
    /// </summary>
    [Test]
    public async Task A_membership_that_is_not_the_last_owner_can_be_revoked()
    {
        var first = await PlatformScenario.ActiveOwnerAsync();
        var second = await SecondAdministratorAsync();
        PlatformScenario.RunAs(first.IdentityId);
        TestApp.SetCurrentTenant(first.PlatformId);
        await StepUpAsync(first);

        var membership = (await TestApp.ListAsync<TenantMembership>()).Single(item => item.IdentityId == second);
        var result = await TestApp.SendAsync(new RevokePlatformAdministratorCommand(membership.Id.Value));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<TenantMembership>()).Single(item => item.Id == membership.Id).Status.ShouldBe(MembershipStatus.Suspended);
    }

    /// <summary>A second administrator, invited by the owner and walking every gate.</summary>
    private static async Task<Guid> SecondAdministratorAsync()
    {
        await TestApp.SendAsync(new InvitePlatformAdministratorCommand("co-owner@example.test"));
        var offer = (await TestApp.ListAsync<PlatformAdminInvitation>()).Single(item => item.NormalizedEmail == "co-owner@example.test");
        var token = await PlatformScenario.SealedTokenAsync(offer.DeliveryMessageId!.Value);

        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        var confirmation = await PlatformScenario.SealedTokenAsync(
            (await PlatformScenario.MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.ConfirmPlatformInviteeCommand(token, confirmation));

        var identity = (await TestApp.ListAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>())
            .Single(user => user.NormalizedEmail == "CO-OWNER@EXAMPLE.TEST");
        PlatformScenario.RunAs(identity.Id);
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));
        await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(token, PlatformScenario.TotpCode(enrollment.Value!.SharedKey)));
        await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(token));
        return identity.Id;
    }

    private static async Task StepUpAsync(PlatformScenario.ActiveOwner owner) =>
        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(owner.SharedKey)))).IsSuccess.ShouldBeTrue();
}
