using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// Administering a standing offer. Neither request has an HTTP route in the first increment — SPEC §6 declares
/// three invitation routes and these are not among them — so they are pinned at the Application boundary, which
/// is where their behaviour has to be settled before a route could ever be added.
/// </summary>
public sealed class ResendAndCancelInvitationTests : TestBase
{
    /// <summary>
    /// A resend is a rotation, not a second send of the same token: the old token stops resolving and the new one
    /// starts, in one transition, so there is never an instant with two usable tokens (IA-REQ-015/017).
    /// </summary>
    [Test]
    public async Task Resending_rotates_the_token_and_delivers_the_new_one()
    {
        var issued = await IssuedAsync();

        var result = await TestApp.SendAsync(new ResendInvitationCommand(issued.Organization.TenantId, issued.InvitationId));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<Invitation>()).ShouldBe(1, "a resend revives the invitation in place");
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.TokenHash.Matches(TestApp.RawTokenAt(1)).ShouldBeTrue();
        invitation.TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeFalse("the superseded token must stop resolving");
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(2, "the rotated token is delivered like the first one");
        (await TestApp.ListAsync<OutboxSecret>()).Count.ShouldBe(2);
        (await TestApp.ListAsync<OutboxMessage>()).ShouldAllBe(message => !message.Payload.Contains(TestApp.RawTokenAt(1)));
    }

    /// <summary>A lapsed invitation is precisely what a resend exists to revive, so it must not be a conflict.</summary>
    [Test]
    public async Task Resending_revives_a_lapsed_invitation_in_place()
    {
        var issued = await IssuedAsync();
        await InvitationTestState.ExpireAsync();

        var result = await TestApp.SendAsync(new ResendInvitationCommand(issued.Organization.TenantId, issued.InvitationId));

        result.IsSuccess.ShouldBeTrue();
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow, "reviving means the window moves with the token");
        invitation.IsPendingAt(DateTimeOffset.UtcNow).ShouldBeTrue();
    }

    [Test]
    public async Task Resending_an_invitation_from_another_tenant_is_refused_and_changes_nothing()
    {
        var issued = await IssuedAsync();
        var other = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(other);

        var result = await TestApp.SendAsync(new ResendInvitationCommand(other.TenantId, issued.InvitationId));

        result.IsFailure.ShouldBeTrue("an invitation is only administrable inside its own tenant");
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await InvitationScenario.SingleInvitationAsync()).TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeTrue();
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
    }

    [Test]
    public async Task Cancelling_withdraws_the_offer_and_stops_its_token_resolving()
    {
        var issued = await IssuedAsync();

        var result = await TestApp.SendAsync(new CancelInvitationCommand(issued.Organization.TenantId, issued.InvitationId));

        result.IsSuccess.ShouldBeTrue();
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
        invitation.CancelledAt.ShouldNotBeNull();
        invitation.IsPendingAt(DateTimeOffset.UtcNow).ShouldBeFalse();
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "invitation.cancelled");
    }

    /// <summary>Withdrawing an offer twice is the caller retrying, not a failure.</summary>
    [Test]
    public async Task Cancelling_twice_is_idempotent_and_records_one_withdrawal()
    {
        var issued = await IssuedAsync();
        var command = new CancelInvitationCommand(issued.Organization.TenantId, issued.InvitationId);

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        var replay = await TestApp.SendAsync(command);

        replay.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "invitation.cancelled").ShouldBe(1);
    }

    /// <summary>A withdrawn offer is settled; reviving it would resurrect a decision the tenant already made.</summary>
    [Test]
    public async Task A_withdrawn_invitation_cannot_be_resent()
    {
        var issued = await IssuedAsync();
        (await TestApp.SendAsync(new CancelInvitationCommand(issued.Organization.TenantId, issued.InvitationId))).IsSuccess.ShouldBeTrue();

        var result = await TestApp.SendAsync(new ResendInvitationCommand(issued.Organization.TenantId, issued.InvitationId));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invitation_conflict");
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Cancelled);
    }

    /// <summary>
    /// Withdrawing the standing offer is what frees the recipient's pending slot, which is the escape hatch for
    /// changing what someone was offered.
    /// </summary>
    [Test]
    public async Task Cancelling_frees_the_recipient_slot_so_a_different_offer_can_be_made()
    {
        var issued = await IssuedAsync();
        (await TestApp.SendAsync(new CancelInvitationCommand(issued.Organization.TenantId, issued.InvitationId))).IsSuccess.ShouldBeTrue();

        var reinvite = await TestApp.SendAsync(new InviteMemberCommand(issued.Organization.TenantId, issued.Email, [issued.Organization.SecondRoleId]));

        reinvite.IsSuccess.ShouldBeTrue();
        reinvite.Value!.InvitationId.ShouldNotBe(issued.InvitationId, "a withdrawn offer is history; the new one is a new invitation");
        (await TestApp.CountAsync<Invitation>()).ShouldBe(2, "invitation history survives its withdrawal");
    }

    private sealed record Issued(InvitationScenario.Organization Organization, Guid InvitationId, string Email);

    private static async Task<Issued> IssuedAsync()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersManage);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        var issued = await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]));
        issued.IsSuccess.ShouldBeTrue();
        return new Issued(organization, issued.Value!.InvitationId, email);
    }
}
