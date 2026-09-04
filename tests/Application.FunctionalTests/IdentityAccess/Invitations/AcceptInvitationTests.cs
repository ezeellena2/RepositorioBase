using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// The authenticated half of IA-REQ-016. The permission this request carries is application-scoped and grants any
/// signed-in identity the right to *attempt* an acceptance — it proves nothing about who they are. Every real gate
/// therefore lives here: a token that resolves, an email that is confirmed, and an email that is the recipient's.
/// </summary>
public sealed class AcceptInvitationTests : TestBase
{
    [Test]
    public async Task Accepting_creates_exactly_one_active_membership_carrying_the_offered_roles()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);

        var result = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        result.IsSuccess.ShouldBeTrue();
        result.Value!.TenantId.ShouldBe(invited.Organization.TenantId.Value);
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single(item => item.IdentityId == invited.RecipientId);
        result.Value.MembershipId.ShouldBe(membership.Id.Value);
        membership.TenantId.ShouldBe(invited.Organization.TenantId);
        membership.Status.ShouldBe(MembershipStatus.Active, "an accepted invitation is an active membership, not another pending state");
        (await TestApp.ListAsync<MembershipRole>())
            .Where(assignment => assignment.MembershipId == membership.Id)
            .Select(assignment => assignment.RoleId.Value)
            .ShouldBe([invited.Organization.RoleId], "the membership carries exactly what the invitation offered");
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedByIdentityId.ShouldBe(invited.RecipientId);
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "invitation.accepted");
    }

    /// <summary>
    /// The second acceptance criterion of the increment, verbatim: accepting the same token twice leaves exactly
    /// one membership and the replay answers idempotently rather than claiming a conflict or an expiry.
    /// </summary>
    [Test]
    public async Task Accepting_the_same_invitation_twice_returns_the_same_membership_and_creates_no_duplicate()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);

        var first = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));
        var replay = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value!.MembershipId.ShouldBe(first.Value!.MembershipId);
        replay.Value.TenantId.ShouldBe(first.Value.TenantId);
        (await TestApp.ListAsync<TenantMembership>()).Count(item => item.IdentityId == invited.RecipientId).ShouldBe(1);
        (await TestApp.ListAsync<MembershipRole>()).Count(assignment => assignment.RoleId.Value == invited.Organization.RoleId).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "invitation.accepted").ShouldBe(1, "a replay is not a second membership change");
    }

    /// <summary>
    /// Holding the token is not being the recipient. Without this the invitation is a bearer credential that lets
    /// whoever intercepts it join the organization under their own identity.
    /// </summary>
    [Test]
    public async Task An_identity_whose_email_is_not_the_recipient_cannot_accept_and_creates_nothing()
    {
        var invited = await InvitedAsync();
        var bystander = await InvitationScenario.SeedConfirmedRecipientAsync($"bystander-{Guid.NewGuid():N}@example.test");
        ActAs(bystander);

        var result = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await TestApp.ListAsync<TenantMembership>()).ShouldNotContain(item => item.IdentityId == bystander);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending, "a refused acceptance leaves the invitation usable by its recipient");
    }

    /// <summary>
    /// The recipient's address matters in its canonical form, not its typed one — the same rule the aggregate
    /// applies when the invitation is issued.
    /// </summary>
    [Test]
    public async Task The_recipient_is_matched_in_canonical_form_rather_than_as_typed()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"Recipient-{Guid.NewGuid():N}@Example.Test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, $"  {email}  ", [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        var recipientId = await InvitationScenario.SeedConfirmedRecipientAsync(email.ToLowerInvariant());
        ActAs(recipientId);

        var result = await TestApp.SendAsync(new AcceptInvitationCommand(TestApp.RawTokenAt(0)));

        result.IsSuccess.ShouldBeTrue("two spellings of one address are one recipient");
        (await TestApp.ListAsync<TenantMembership>()).ShouldContain(item => item.IdentityId == recipientId);
    }

    /// <summary>IA-REQ-005: an unconfirmed identity may not accept, however valid the token is.</summary>
    [Test]
    public async Task An_unconfirmed_identity_cannot_accept_and_creates_nothing()
    {
        var invited = await InvitedAsync(confirmRecipient: false);
        ActAs(invited.RecipientId);

        var result = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await TestApp.ListAsync<TenantMembership>()).ShouldNotContain(item => item.IdentityId == invited.RecipientId);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
    }

    /// <summary>
    /// A lapsed invitation and a withdrawn one are both simply unusable. Telling them apart tells a token holder
    /// what the organization did after inviting them, which SPEC §6 forbids.
    /// </summary>
    [Test]
    public async Task A_lapsed_invitation_and_a_withdrawn_one_are_refused_identically()
    {
        var lapsed = await InvitedAsync();
        ActAs(lapsed.RecipientId);
        await InvitationTestState.ExpireAsync();

        var expired = await TestApp.SendAsync(new AcceptInvitationCommand(lapsed.Token));

        await InvitationTestState.CancelAsync();
        var cancelled = await TestApp.SendAsync(new AcceptInvitationCommand(lapsed.Token));

        expired.IsFailure.ShouldBeTrue();
        expired.Error!.Code.ShouldBe("invalid_invitation");
        cancelled.IsFailure.ShouldBeTrue();
        cancelled.Error!.Code.ShouldBe(expired.Error.Code);
        cancelled.Error.Category.ShouldBe(expired.Error.Category);
        (await TestApp.ListAsync<TenantMembership>()).ShouldNotContain(item => item.IdentityId == lapsed.RecipientId);
    }

    [Test]
    public async Task An_unknown_token_is_refused_without_disclosing_that_it_never_existed()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);

        var unknown = await TestApp.SendAsync(new AcceptInvitationCommand(TestApp.RawTokenAt(11)));
        await InvitationTestState.CancelAsync();
        var withdrawn = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        unknown.IsFailure.ShouldBeTrue();
        unknown.Error!.Code.ShouldBe(withdrawn.Error!.Code);
        unknown.Error.Category.ShouldBe(withdrawn.Error.Category);
    }

    /// <summary>
    /// The aggregate answers a replay idempotently only for the identity that accepted. A different identity
    /// arriving at a settled invitation is a genuine conflict, not a silent success and not an unhandled throw.
    /// </summary>
    [Test]
    public async Task An_invitation_another_identity_already_accepted_is_a_conflict()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);
        (await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token))).IsSuccess.ShouldBeTrue();

        var intruder = await InvitationScenario.SeedConfirmedRecipientAsync($"intruder-{Guid.NewGuid():N}@example.test");
        ActAs(intruder);
        var result = await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBeOneOf("invalid_invitation", "invitation_conflict");
        (await TestApp.ListAsync<TenantMembership>()).ShouldNotContain(item => item.IdentityId == intruder);
    }

    /// <summary>
    /// Two acceptances of one invitation race on the invitation's row version and on the membership uniqueness
    /// index. Exactly one membership may exist afterwards, and neither caller may see a raw database failure.
    /// </summary>
    [Test]
    public async Task Concurrent_acceptances_of_one_invitation_leave_exactly_one_membership()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);
        var command = new AcceptInvitationCommand(invited.Token);
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)));

        results.ShouldAllBe(result => result.IsSuccess || result.Error!.Code == "invitation_conflict");
        (await TestApp.ListAsync<TenantMembership>()).Count(item => item.IdentityId == invited.RecipientId).ShouldBe(1);
        (await TestApp.ListAsync<MembershipRole>()).Count(assignment => assignment.RoleId.Value == invited.Organization.RoleId).ShouldBe(1);
    }

    /// <summary>The membership and the invitation transition together, or neither does (IA-REQ-016/033).</summary>
    [Test]
    public async Task A_failure_after_persistence_rolls_back_the_membership_with_the_acceptance()
    {
        var invited = await InvitedAsync();
        ActAs(invited.RecipientId);
        TestApp.ForceInvitationRollbackAfterPersistedEffects();

        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(new AcceptInvitationCommand(invited.Token)));

        (await TestApp.ListAsync<TenantMembership>()).ShouldNotContain(item => item.IdentityId == invited.RecipientId);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);

        (await TestApp.SendAsync(new AcceptInvitationCommand(invited.Token))).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<TenantMembership>()).Count(item => item.IdentityId == invited.RecipientId).ShouldBe(1);
    }

    private sealed record Invited(InvitationScenario.Organization Organization, Guid RecipientId, string Token);

    private static async Task<Invited> InvitedAsync(bool confirmRecipient = true)
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        var recipientId = confirmRecipient
            ? await InvitationScenario.SeedConfirmedRecipientAsync(email)
            : await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        return new Invited(organization, recipientId, TestApp.RawTokenAt(0));
    }

    /// <summary>
    /// Acceptance is identity-scoped: the caller is signed in but holds no membership and therefore no active
    /// tenant, which is exactly the state the request has to work from.
    /// </summary>
    private static void ActAs(Guid identityId)
    {
        TestApp.SetUserId(identityId);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
    }

    private static async Task<Application.Common.Models.Result<AcceptedInvitation>> SendFromIndependentScopeAsync(AcceptInvitationCommand command, Barrier barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await sender.Send(command);
    }
}
