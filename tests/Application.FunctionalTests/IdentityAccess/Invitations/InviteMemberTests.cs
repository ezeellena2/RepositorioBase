using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

public sealed class InviteMemberTests : TestBase
{
    [Test]
    public async Task Inviting_a_member_issues_one_pending_invitation_offering_the_requested_roles()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"ANA-{Guid.NewGuid():N}@Example.Test";

        var result = await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.SecondRoleId]));

        result.IsSuccess.ShouldBeTrue();
        var invitation = await InvitationScenario.SingleInvitationAsync();
        result.Value!.InvitationId.ShouldBe(invitation.Id.Value);
        result.Value.ExpiresAt.ShouldBe(invitation.ExpiresAt);
        result.Value.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow, "an invitation that is already expired was never an invitation");
        invitation.TenantId.ShouldBe(organization.TenantId);
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.NormalizedEmail.ShouldBe(email.ToLowerInvariant(), "the recipient reaches persistence in exactly one canonical form");
        invitation.Roles.Select(offered => offered.RoleId.Value).ShouldBe([organization.SecondRoleId]);
        await InvitationScenario.AssertDeliveryIntentAsync(TestApp.RawTokenAt(0));
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(
            item => item.EventType == "invitation.issued" && item.TenantId == organization.TenantId,
            "an invitation is an audited membership change (IA-REQ-026)");
    }

    /// <summary>
    /// IA-REQ-018 is a rule about what is written, not only about what is returned: the delivery intent carries an
    /// opaque reference and the token lives only in the encrypted envelope. Asserting the payload deserializes to
    /// the invitation without carrying its token is what makes that checkable.
    /// </summary>
    [Test]
    public async Task An_issued_invitation_never_puts_its_token_in_the_result_or_the_payload()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization));

        result.IsSuccess.ShouldBeTrue();
        var invitation = await InvitationScenario.SingleInvitationAsync();
        var outbox = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).Single();
        // The message references the invitation it will deliver, by identifier and never by token.
        outbox.Payload.ShouldContain(invitation.Id.Value.ToString());
        await InvitationScenario.AssertDeliveryIntentAsync(TestApp.RawTokenAt(0));
        invitation.TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeTrue("the stored hash is the hash of the token that was minted");
    }

    /// <summary>
    /// The first acceptance criterion of the increment, verbatim: an identity with <c>members.invite</c> in one
    /// organization gets no invitation and no delivery intent out of another.
    /// </summary>
    [Test]
    public async Task Inviting_into_a_tenant_the_caller_does_not_administer_is_denied_and_creates_nothing()
    {
        var administered = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        var other = await InvitationScenario.SeedOrganizationAsync();
        InvitationScenario.ActAs(administered);

        var result = await TestApp.SendAsync(NewCommand(administered) with { TenantId = other.TenantId });

        result.IsFailure.ShouldBeTrue("the route tenant is compared against the active tenant, never trusted");
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    [Test]
    public async Task Inviting_without_members_invite_is_denied_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync();
        InvitationScenario.ActAs(organization, permissionGranted: false);

        await Should.ThrowAsync<Application.Common.Exceptions.ForbiddenAccessException>(
            () => TestApp.SendAsync(NewCommand(organization)));

        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    [Test]
    public async Task Offering_a_role_that_belongs_to_another_tenant_is_rejected_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        var other = await InvitationScenario.SeedOrganizationAsync();
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { RoleIds = [other.RoleId] });

        result.IsFailure.ShouldBeTrue("an invitation offers roles from its own tenant only (IA-REQ-014/034)");
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    [Test]
    public async Task Offering_a_role_that_does_not_exist_is_rejected_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { RoleIds = [Guid.NewGuid()] });

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    /// <summary>
    /// The aggregate throws for an empty offer, which would surface as a 500. An invitation with no roles is bad
    /// input, so it has to be refused as one before the aggregate is built.
    /// </summary>
    [Test]
    public async Task Offering_no_role_at_all_is_rejected_as_input_rather_than_thrown()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { RoleIds = [] });

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    [Test]
    public async Task A_recipient_that_is_not_an_address_is_rejected_as_input_rather_than_thrown()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { Email = "not-an-address" });

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    /// <summary>
    /// IA-REQ-017: unlimited equivalent active tokens are not allowed. Re-inviting the same recipient on the same
    /// terms therefore rotates the one pending invitation rather than opening a second, and the previous token
    /// stops resolving the instant the new one exists.
    /// </summary>
    [Test]
    public async Task Re_inviting_the_same_recipient_on_the_same_terms_rotates_the_one_pending_invitation()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var command = NewCommand(organization);

        var first = await TestApp.SendAsync(command);
        var second = await TestApp.SendAsync(command with { Email = command.Email.ToUpperInvariant() });

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value!.InvitationId.ShouldBe(first.Value!.InvitationId, "the recipient's pending slot is revived, not duplicated");
        (await TestApp.CountAsync<Invitation>()).ShouldBe(1);
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.TokenHash.Matches(TestApp.RawTokenAt(1)).ShouldBeTrue("the rotated token is the one that resolves");
        invitation.TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeFalse("the previous token must stop resolving the moment it is replaced");
        (await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).ShouldBe(2, "each rotation is delivered (IA-REQ-017/027)");
    }

    /// <summary>
    /// A different offer replaces the standing one atomically rather than conflicting with it. Refusing was the
    /// first design and it was wrong: withdrawing an invitation needs <c>members.manage</c> and has no route in
    /// the first increment, so a holder of <c>members.invite</c> who mistyped a role would have been stuck with
    /// the wrong offer forever. Replacement leaves exactly one live offer, and the superseded token and its
    /// delivery stop being usable in the same transaction (IA-REQ-015/017).
    /// </summary>
    [Test]
    public async Task Re_inviting_the_same_recipient_with_a_different_offer_replaces_the_standing_one()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var command = NewCommand(organization);

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        var second = await TestApp.SendAsync(command with { RoleIds = [organization.SecondRoleId] });

        second.IsSuccess.ShouldBeTrue();
        var live = (await TestApp.ListAsync<Invitation>()).Where(item => item.IsPendingAt(DateTimeOffset.UtcNow)).ToArray();
        live.Length.ShouldBe(1, "a recipient never holds two live offers");
        second.Value!.InvitationId.ShouldBe(live[0].Id.Value);
        live[0].TokenHash.Matches(TestApp.RawTokenAt(1)).ShouldBeTrue("the replacement carries the new token");
        live[0].TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeFalse("the superseded token stops resolving");
        await AssertOnlyTheNewestSecretIsUsableAsync();
    }

    /// <summary>
    /// Whoever supersedes an offer owns invalidating what it replaced. Leaving the previous envelope pending would
    /// let the worker deliver a token the tenant has already withdrawn (IA-REQ-015/018).
    /// </summary>
    private static async Task AssertOnlyTheNewestSecretIsUsableAsync()
    {
        var secrets = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>())
            .OrderBy(secret => secret.ExpiresAt)
            .ToArray();
        secrets.Length.ShouldBeGreaterThan(1, "each offer writes its own delivery envelope");
        foreach (var superseded in secrets[..^1])
        {
            superseded.Status.ShouldNotBe(Domain.IdentityAccess.Outbox.OutboxSecretStatus.Pending, "a superseded envelope must not stay deliverable");
            superseded.Ciphertext.ShouldBeNull("terminalizing an envelope clears the token it was holding");
        }

        secrets[^1].Status.ShouldBe(Domain.IdentityAccess.Outbox.OutboxSecretStatus.Pending);
    }

    /// <summary>
    /// IA-REQ-016 allows at most one membership per identity and tenant, and the unique index enforces it. Finding
    /// that out at acceptance time wastes an invitation and a delivery; it is knowable when the offer is made.
    /// </summary>
    [Test]
    public async Task Inviting_someone_who_already_holds_a_membership_is_a_conflict_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        var email = $"member-{Guid.NewGuid():N}@example.test";
        var memberId = await InvitationScenario.SeedConfirmedRecipientAsync(email);
        await SeedMembershipAsync(organization.TenantId, memberId);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { Email = email });

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invitation_conflict");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    /// <summary>
    /// IA-REQ-005: an unconfirmed identity may not invite. The pipeline only proves the permission, so the gate
    /// has to be the handler's.
    /// </summary>
    [Test]
    public async Task An_inviter_whose_email_is_not_confirmed_is_denied_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        await UnconfirmAsync(organization.InviterIdentityId);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    /// <summary>
    /// Two competing invites for one recipient race on the partial unique index. One must win and the other must
    /// answer as a typed conflict, never as the raw unique violation the database raises.
    /// </summary>
    [Test]
    public async Task Concurrent_invitations_for_one_recipient_leave_a_single_pending_row_and_no_unhandled_violation()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var command = NewCommand(organization);
        TestApp.EnableInvitationLockBarrier();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)));

        TestApp.InvitationLockBarrierWasObserved.ShouldBeTrue("both invitations must have decided from the same committed state");
        results.ShouldAllBe(result => result.IsSuccess || result.Error!.Code == "invitation_conflict");
        (await TestApp.CountAsync<Invitation>()).ShouldBe(1, "the recipient holds one pending slot however the race lands");
    }

    /// <summary>
    /// IA-REQ-017 makes the invitation and its delivery intent one change. If the transaction rolls back after
    /// both are persisted, neither may survive, and a retry must still produce exactly one of each.
    /// </summary>
    [Test]
    public async Task A_failure_after_persistence_rolls_back_the_invitation_with_its_delivery_intent()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var command = NewCommand(organization);
        TestApp.ForceInvitationRollbackAfterPersistedEffects();

        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(command));

        await InvitationScenario.AssertNoInvitationEffectsAsync();

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<Invitation>()).ShouldBe(1);
        (await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).ShouldBe(1);
        (await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxSecret>()).ShouldBe(1);
    }

    /// <summary>
    /// IA-REQ-047. Without this rule <c>members.invite</c> is a master key: whoever holds it can offer a role that
    /// carries permissions they do not have — up to and including one that can administer roles — and then hold
    /// those permissions themselves through a second account they control.
    /// </summary>
    [Test]
    public async Task Offering_a_role_that_grants_more_than_the_inviter_holds_is_refused_and_creates_nothing()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        await InvitationGrants.GrantAsync(organization.SecondRoleId, Permissions.RolesManage);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { RoleIds = [organization.SecondRoleId] });

        result.IsFailure.ShouldBeTrue("an invitation cannot hand out authority the inviter does not have");
        result.Error!.Code.ShouldBe("invalid_invitation");
        await InvitationScenario.AssertNoInvitationEffectsAsync();
    }

    /// <summary>
    /// The converse, so the rule is a subset test and not a blanket refusal of any role but the inviter's own: a
    /// role carrying strictly less than the inviter holds is offerable.
    /// </summary>
    [Test]
    public async Task Offering_a_role_within_what_the_inviter_holds_is_allowed()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersRead);
        await InvitationGrants.GrantAsync(organization.SecondRoleId, Permissions.MembersRead);
        InvitationScenario.ActAs(organization);

        var result = await TestApp.SendAsync(NewCommand(organization) with { RoleIds = [organization.SecondRoleId] });

        result.IsSuccess.ShouldBeTrue();
        (await InvitationScenario.SingleInvitationAsync()).Roles.Select(offered => offered.RoleId.Value).ShouldBe([organization.SecondRoleId]);
    }

    /// <summary>Reissuing re-offers the same roles, so it is bound by the same rule as the original offer.</summary>
    [Test]
    public async Task A_role_that_stops_being_grantable_cannot_be_re_offered()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersRead);
        await InvitationGrants.GrantAsync(organization.SecondRoleId, Permissions.MembersRead);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.SecondRoleId]))).IsSuccess.ShouldBeTrue();
        await InvitationGrants.RevokeAsync(organization.RoleId, Permissions.MembersRead);

        var result = await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.SecondRoleId]));

        result.IsFailure.ShouldBeTrue("an offer the inviter can no longer make cannot be renewed either");
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await InvitationScenario.SingleInvitationAsync()).TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeTrue("a refused reissue does not rotate the token");
    }

    private static InviteMemberCommand NewCommand(InvitationScenario.Organization organization) =>
        new(organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.RoleId]);

    private static async Task<Application.Common.Models.Result<IssuedInvitation>> SendFromIndependentScopeAsync(InviteMemberCommand command, Barrier barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await sender.Send(command);
    }

    private static async Task SeedMembershipAsync(TenantId tenantId, Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == tenantId);
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        membership.Activate(tenant);
        context.Add(membership);
        await context.SaveChangesAsync();
    }

    private static async Task UnconfirmAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Users.Where(user => user.Id == identityId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailConfirmed, false));
    }
}
