using System.Globalization;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// Administering a standing offer. The behaviour is pinned at the Application boundary, where it has to be
/// settled whatever route reaches it; `InvitationHttpContractTests` pins the two routes Task 25 added on top of
/// it. The last two cases are contention rather than ordering: two administrators really deciding from the same
/// committed state, which is the only way to tell a rule from a lucky sequence.
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
        (await TestApp.ListAsync<OutboxMessage>()).ShouldAllBe(message => !message.Payload.Contains(TestApp.RawTokenAt(1)));

        // Whoever rotates the token owns invalidating the envelope it replaced: leaving it pending would let the
        // worker deliver a token that no longer resolves (IA-REQ-015/018).
        var secrets = (await TestApp.ListAsync<OutboxSecret>()).OrderBy(secret => secret.ExpiresAt).ToArray();
        secrets.Length.ShouldBe(2);
        secrets[0].Status.ShouldNotBe(OutboxSecretStatus.Pending, "the superseded envelope must not stay deliverable");
        secrets[0].Ciphertext.ShouldBeNull("terminalizing clears the token the envelope was holding");
        secrets[1].Status.ShouldBe(OutboxSecretStatus.Pending);
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

        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secret.Status.ShouldNotBe(OutboxSecretStatus.Pending, "withdrawing an offer withdraws its undelivered token too");
        secret.Ciphertext.ShouldBeNull();
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

    /// <summary>
    /// IA-REQ-047 binds every path that establishes an offer, and a resend re-offers the same roles under a new
    /// token. Checking only the issue path would leave resend as a way to keep an offer alive after the inviter
    /// stopped being able to make it.
    /// </summary>
    [Test]
    public async Task Resending_an_offer_the_inviter_can_no_longer_grant_is_refused()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite, Permissions.MembersManage, Permissions.MembersRead);
        await InvitationGrants.GrantAsync(organization.SecondRoleId, Permissions.MembersRead);
        InvitationScenario.ActAs(organization);
        var issued = await TestApp.SendAsync(new InviteMemberCommand(
            organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.SecondRoleId]));
        issued.IsSuccess.ShouldBeTrue();
        await InvitationGrants.RevokeAsync(organization.RoleId, Permissions.MembersRead);

        var result = await TestApp.SendAsync(new ResendInvitationCommand(organization.TenantId, issued.Value!.InvitationId));

        result.IsFailure.ShouldBeTrue("a resend re-offers the roles, so it is bound by the same subset rule");
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await InvitationScenario.SingleInvitationAsync()).TokenHash.Matches(TestApp.RawTokenAt(0)).ShouldBeTrue("a refused resend does not rotate the token");
    }

    /// <summary>
    /// Withdrawing and reissuing the same offer at the same time. Whichever commits first, the offer must end in
    /// one state and no superseded envelope may still be deliverable — an undelivered token for an offer that was
    /// withdrawn is exactly the mail nobody may ever receive (IA-REQ-015/017/018).
    /// </summary>
    [Test]
    public async Task Withdrawing_and_reissuing_one_offer_at_the_same_time_settle_it_once()
    {
        var issued = await IssuedAsync();
        TestApp.EnableInvitationLockBarrier();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => RaceAsync(sender => sender.Send(new CancelInvitationCommand(issued.Organization.TenantId, issued.InvitationId)), barrier)),
            Task.Run(() => RaceAsync(sender => sender.Send(new ResendInvitationCommand(issued.Organization.TenantId, issued.InvitationId)), barrier)));

        TestApp.InvitationLockBarrierWasObserved.ShouldBeTrue("both administrators must have decided from the same committed offer");
        results.ShouldAllBe(result => result.IsSuccess || result.Error!.Code == "invitation_conflict");
        results.Count(result => result.IsSuccess).ShouldBeGreaterThan(0, "one of the two really happened");

        var invitation = await InvitationScenario.SingleInvitationAsync();
        var secrets = await TestApp.ListAsync<OutboxSecret>();
        if (invitation.Status == InvitationStatus.Cancelled)
        {
            secrets.ShouldAllBe(secret => secret.Status != OutboxSecretStatus.Pending, "a withdrawn offer leaves no deliverable token");
            secrets.ShouldAllBe(secret => secret.Ciphertext == null);
        }
        else
        {
            invitation.Status.ShouldBe(InvitationStatus.Pending, "the only settled states are the two that were asked for");
            secrets.Count(secret => secret.Status == OutboxSecretStatus.Pending)
                .ShouldBe(1, "a reissue leaves exactly the one envelope its own token is in");
        }
    }

    /// <summary>
    /// Widening a role while the offer naming it is being reissued. The widening cancels every offer of that role
    /// in its own transaction, so the outcome is the same whichever order they commit in: no live offer of the
    /// widened role, and no undelivered token left for one (IA-REQ-047, IA-REQ-053).
    /// </summary>
    [Test]
    public async Task Widening_a_role_while_its_offer_is_reissued_leaves_no_live_offer_of_it()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(
            Permissions.MembersInvite, Permissions.MembersManage, Permissions.RolesManage, Permissions.MembersRead);
        InvitationScenario.ActAs(organization);
        await SignedInAsync(organization.InviterIdentityId);
        var issued = await TestApp.SendAsync(new InviteMemberCommand(
            organization.TenantId, $"invitee-{Guid.NewGuid():N}@example.test", [organization.SecondRoleId]));
        issued.IsSuccess.ShouldBeTrue();
        var offered = await RoleStateAsync(organization.SecondRoleId);
        await IssueProofAsync(ProofActions.RoleChange);
        TestApp.EnableInvitationLockBarrier();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => RaceAsync(sender => sender.Send(new UpdateRoleCommand(
                organization.TenantId, organization.SecondRoleId, offered.Name, [Permissions.MembersRead], offered.Version)), barrier)),
            Task.Run(() => RaceAsync(sender => sender.Send(new ResendInvitationCommand(organization.TenantId, issued.Value!.InvitationId)), barrier)));

        TestApp.InvitationLockBarrierWasObserved.ShouldBeTrue("the widening and the reissue must have decided from the same committed offer");
        results.ShouldAllBe(result => result.IsSuccess || result.Error!.Code == "role_concurrency_conflict" || result.Error.Code == "invitation_conflict");

        // Exactly one of the two commits, and the loser is refused rather than half-applied. When the reissue
        // wins, the role was never widened — so the offer it revived is legitimately still standing, and the rule
        // has to hold on the retry instead: the same widening, from the state the reissue left, still withdraws it.
        if (results[0].IsFailure)
        {
            (await OfferedCodesAsync(organization.SecondRoleId)).ShouldBeEmpty("a refused widening confers nothing");
            (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
            var retried = await RoleStateAsync(organization.SecondRoleId);
            await IssueProofAsync(ProofActions.RoleChange);
            (await TestApp.SendAsync(new UpdateRoleCommand(
                organization.TenantId, organization.SecondRoleId, retried.Name, [Permissions.MembersRead], retried.Version))).IsSuccess.ShouldBeTrue();
        }

        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(
            InvitationStatus.Cancelled, "an offer of a widened role is withdrawn whichever way the race went");
        (await TestApp.ListAsync<OutboxSecret>()).ShouldAllBe(
            secret => secret.Status != OutboxSecretStatus.Pending, "no token for a withdrawn offer may still be delivered");
    }

    private static async Task<string[]> OfferedCodesAsync(Guid roleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identifier = RoleId.From(roleId);
        return await context.RolePermissions.AsNoTracking()
            .Where(permission => permission.RoleId == identifier)
            .Select(permission => permission.PermissionCode)
            .ToArrayAsync();
    }

    /// <summary>The role's own name and concurrency token, read the way the write path reads them.</summary>
    private static async Task<(string Name, string Version)> RoleStateAsync(Guid roleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identifier = RoleId.From(roleId);
        var role = await context.TenantRoles.SingleAsync(candidate => candidate.Id == identifier);
        return (role.Name, context.Entry(role).Property<uint>("Version").CurrentValue.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A real persisted session behind the request. A proof belongs to one session by foreign key, so a made-up
    /// identifier would be refused by the database before the case under test ever ran.
    /// </summary>
    private static async Task SignedInAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = UserSession.Create(identityId, DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(8));
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        TestApp.SetSessionId(session.Id.Value);
    }

    /// <summary>
    /// The proof a sensitive role write spends (amendment D2). It is issued rather than bought here because what
    /// this case is about is contention, and buying one is already pinned by its own tests.
    /// </summary>
    private static async Task IssueProofAsync(string action)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRecentIdentityProofStore>().IssueAsync(
            TestApp.GetUserId()!.Value,
            UserSessionId.From(TestApp.GetSessionId()!.Value),
            action,
            RecentIdentityProofMethod.Password,
            null,
            CancellationToken.None);
    }

    /// <summary>
    /// One request from its own scope, released with the other. Two tasks started together still serialize by
    /// accident; this barrier starts them together and the invitation lock barrier holds them both until each has
    /// read the same committed rows.
    /// </summary>
    private static async Task<Result> RaceAsync<T>(Func<ISender, Task<T>> send, Barrier barrier) where T : Result
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await send(sender);
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
