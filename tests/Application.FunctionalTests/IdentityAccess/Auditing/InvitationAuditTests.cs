using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Domain.IdentityAccess.Auditing;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Auditing;

/// <summary>
/// IA-REQ-026 for invitations. The metadata allowlist admits only <c>code</c> and <c>outcome</c>, so these assert
/// the exact dictionary rather than a subset: an extra key is either a leak or an unreviewed contract change, and
/// a missing one makes the record unreadable to whoever has to answer for the membership later.
/// </summary>
public sealed class InvitationAuditTests : TestBase
{
    [Test]
    public async Task Issuing_an_invitation_persists_the_exact_invitation_issued_event()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);

        var issued = await TestApp.SendAsync(new InviteMemberCommand(
            organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.RoleId]));
        issued.IsSuccess.ShouldBeTrue();

        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "invitation.issued");
        audit.TenantId.ShouldBe(organization.TenantId);
        audit.ActorId.ShouldBe(organization.InviterIdentityId, "the inviter answers for the offer they made");
        audit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        audit.OccurredAt.ShouldNotBe(default);
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "invitation.issued", ["outcome"] = "issued" });
    }

    [Test]
    public async Task Accepting_an_invitation_persists_the_exact_invitation_accepted_event()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        var recipientId = await InvitationScenario.SeedConfirmedRecipientAsync(email);
        TestApp.SetUserId(recipientId);
        TestApp.SetCurrentTenant(null);
        (await TestApp.SendAsync(new AcceptInvitationCommand(TestApp.RawTokenAt(0)))).IsSuccess.ShouldBeTrue();

        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "invitation.accepted");
        audit.TenantId.ShouldBe(organization.TenantId);
        audit.ActorId.ShouldBe(recipientId, "the accepting identity is the actor, not the inviter");
        audit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "invitation.accepted", ["outcome"] = "accepted" });
    }

    /// <summary>
    /// A superseded offer and a withdrawn one are membership changes too, and the outcome is what distinguishes
    /// them from a first offer in a record someone reads months later.
    /// </summary>
    [Test]
    public async Task Superseding_and_withdrawing_record_their_own_outcomes()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersManage);
        InvitationScenario.ActAs(organization);
        var command = new InviteMemberCommand(organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.RoleId]);

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        var superseded = await TestApp.SendAsync(command with { RoleIds = [organization.SecondRoleId] });
        superseded.IsSuccess.ShouldBeTrue();

        var issued = (await TestApp.ListAsync<AuditEvent>()).Where(item => item.EventType == "invitation.issued").ToArray();
        issued.Length.ShouldBe(2);
        issued.Select(item => item.Metadata["outcome"]).Order(StringComparer.Ordinal).ShouldBe(["issued", "superseded"]);

        (await TestApp.SendAsync(new Application.IdentityAccess.Invitations.CancelInvitation.CancelInvitationCommand(
            organization.TenantId, superseded.Value!.InvitationId))).IsSuccess.ShouldBeTrue();

        var cancelled = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "invitation.cancelled");
        cancelled.ActorId.ShouldBe(organization.InviterIdentityId);
        cancelled.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "invitation.cancelled", ["outcome"] = "cancelled" });
    }

    /// <summary>No invitation audit record may carry the token, the recipient's address, or anything else unsafe.</summary>
    [Test]
    public async Task No_invitation_audit_record_carries_a_secret_or_the_recipient_address()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();

        foreach (var audit in await TestApp.ListAsync<AuditEvent>())
        {
            audit.Metadata.Keys.Order(StringComparer.Ordinal).ShouldBeSubsetOf(["code", "outcome", "reason"]);
            foreach (var value in audit.Metadata.Values.Append(audit.CorrelationId))
            {
                value.ShouldNotContain(TestApp.RawTokenAt(0));
                value.ShouldNotContain(email, Case.Insensitive);
            }
        }
    }
}
