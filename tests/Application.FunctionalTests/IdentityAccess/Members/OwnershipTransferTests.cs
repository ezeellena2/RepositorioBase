using System.Net;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>
/// Giving an `Organization` away (IA-REQ-053).
/// <para>
/// It is the one change nobody can undo alone, so it carries more conditions than anything else in C5: the
/// permission, being the current owner, an active recipient in the same organization, a live proof, and the row's
/// own version — all of it in one transaction, or none of it.
/// </para>
/// </summary>
public sealed class OwnershipTransferTests : TestBase
{
    [Test]
    public async Task The_owner_hands_the_organization_to_another_active_member()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var recipient = await scenario.AddQuietMemberAsync("successor", Permissions.MembersRead);

        var transferred = await TransferAsync(scenario.Owner, scenario, recipient, await VersionAsync(scenario, recipient));

        transferred.StatusCode.ShouldBe(HttpStatusCode.NoContent, await transferred.Content.ReadAsStringAsync());
        var members = await MembersAsync(scenario);
        members.Single(member => member.IsOwner).MembershipId.ShouldBe(recipient);
        members.Single(member => member.MembershipId == scenario.OwnerMembershipId).IsOwner
            .ShouldBeFalse("an organization has one owner, so naming the next one unnames the last");
    }

    /// <summary>
    /// Holding `tenant.ownership.transfer` is necessary and never sufficient. Somebody handed the permission by a
    /// role still cannot give away an organization that is not theirs.
    /// </summary>
    [Test]
    public async Task Somebody_who_is_not_the_owner_cannot_give_the_organization_away()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var pretender = await scenario.AddMemberAsync("pretender", Permissions.TenantOwnershipTransfer, Permissions.MembersRead);

        var refused = await TransferAsync(pretender.Acting, scenario, pretender.MembershipId, await VersionAsync(scenario, pretender.MembershipId));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("owner_required");
        (await MembersAsync(scenario)).Single(member => member.IsOwner).MembershipId.ShouldBe(scenario.OwnerMembershipId);
    }

    /// <summary>
    /// Being refused for who you are must not cost a proof, so the owner check runs before the proof is spent.
    /// <para>
    /// The proof is a server-side row nobody can read, so the only way to observe that it survived the refusal is
    /// to use it: the same session, having bought exactly one proof and been refused once, transfers the moment
    /// it really is the owner. Spend the proof on that refusal and this last step answers 401 instead.
    /// </para>
    /// </summary>
    [Test]
    public async Task Being_refused_for_not_being_the_owner_spends_no_proof()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var pretender = await scenario.AddMemberAsync("pretender", Permissions.TenantOwnershipTransfer, Permissions.MembersRead);
        await pretender.Acting.ProveAsync(ProofActions.OwnershipTransfer);

        var refused = await TransferWithoutProvingAsync(pretender.Acting, scenario, pretender.MembershipId, await VersionAsync(scenario, pretender.MembershipId));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await TransferAsync(scenario.Owner, scenario, pretender.MembershipId, await VersionAsync(scenario, pretender.MembershipId)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var handedBack = await TransferWithoutProvingAsync(pretender.Acting, scenario, scenario.OwnerMembershipId, await VersionAsync(scenario, scenario.OwnerMembershipId));

        handedBack.StatusCode.ShouldBe(HttpStatusCode.NoContent, await handedBack.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task A_transfer_without_a_proof_is_refused_and_moves_nothing()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var recipient = await scenario.AddQuietMemberAsync("successor", Permissions.MembersRead);

        var unproved = await TransferWithoutProvingAsync(scenario.Owner, scenario, recipient, await VersionAsync(scenario, recipient));

        unproved.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(unproved)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await MembersAsync(scenario)).Single(member => member.IsOwner).MembershipId.ShouldBe(scenario.OwnerMembershipId);
    }

    [Test]
    public async Task Ownership_cannot_be_given_to_somebody_who_is_not_an_active_member()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var paused = await scenario.AddQuietMemberAsync("paused", Permissions.MembersRead);
        (await StatusAsync(scenario, paused, "suspend")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refused = await TransferAsync(scenario.Owner, scenario, paused, await VersionAsync(scenario, paused));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_membership_operation");
        (await MembersAsync(scenario)).Single(member => member.IsOwner).MembershipId.ShouldBe(scenario.OwnerMembershipId);
    }

    [Test]
    public async Task Ownership_cannot_be_given_to_a_member_of_another_organization()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        using var elsewhere = await OrganizationScenario.CreateAsync("elsewhere");

        var refused = await TransferAsync(scenario.Owner, scenario, elsewhere.OwnerMembershipId, "1");

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "another organization's membership is absent here rather than forbidden (IA-REQ-030)");
        (await MembersAsync(scenario)).Single(member => member.IsOwner).MembershipId.ShouldBe(scenario.OwnerMembershipId);
        (await MembersAsync(elsewhere)).Single(member => member.IsOwner).MembershipId.ShouldBe(elsewhere.OwnerMembershipId);
    }

    [Test]
    public async Task A_stale_version_moves_nothing()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var recipient = await scenario.AddQuietMemberAsync("successor", Permissions.MembersRead);
        var stale = await VersionAsync(scenario, recipient);
        (await StatusAsync(scenario, recipient, "suspend")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(scenario, recipient, "reactivate")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refused = await TransferAsync(scenario.Owner, scenario, recipient, stale);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("membership_concurrency_conflict");
        (await MembersAsync(scenario)).Single(member => member.IsOwner).MembershipId.ShouldBe(scenario.OwnerMembershipId);
    }

    [Test]
    public async Task Transferring_to_the_member_who_already_owns_it_answers_the_same_and_records_nothing_new()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");

        var again = await TransferAsync(scenario.Owner, scenario, scenario.OwnerMembershipId, await VersionAsync(scenario, scenario.OwnerMembershipId));

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent, "asking for a state that already holds is not a conflict");
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.Metadata.TryGetValue("outcome", out var outcome) && outcome == "ownership-transferred")
            .ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0, "nothing moved, so nobody is told anything");
    }

    /// <summary>
    /// Both people are told, through the outbox in the same transaction. The payload names the tenant and the two
    /// memberships and nothing else — no address, no token, and so no `OutboxSecret` to seal or retire.
    /// </summary>
    [Test]
    public async Task A_transfer_is_audited_and_notified_without_carrying_anything_secret()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var recipient = await scenario.AddQuietMemberAsync("successor", Permissions.MembersRead);

        (await TransferAsync(scenario.Owner, scenario, recipient, await VersionAsync(scenario, recipient)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var audited = (await TestApp.ListAsync<AuditEvent>())
            .Where(entry => entry.EventType == "membership.changed"
                && entry.Metadata.TryGetValue("outcome", out var outcome) && outcome == "ownership-transferred")
            .ToArray();
        audited.ShouldHaveSingleItem().ActorId.ShouldBe(scenario.OwnerId);

        var notice = (await TestApp.ListAsync<OutboxMessage>()).ShouldHaveSingleItem();
        notice.Type.ShouldBe("identity.ownership.transferred.notice.requested");
        notice.Payload.ShouldContain(recipient.ToString());
        notice.Payload.ShouldContain(scenario.OwnerMembershipId.ToString());
        notice.Payload.Contains('@', StringComparison.Ordinal).ShouldBeFalse("no address travels in a payload");
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0, "there is no token here, so there is nothing to seal");
    }

    /// <summary>
    /// The former owner keeps their membership and their roles. Transferring gives the organization away, not the
    /// person's place in it.
    /// </summary>
    [Test]
    public async Task The_former_owner_stays_a_member_with_what_they_held()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var recipient = await scenario.AddQuietMemberAsync("successor", Permissions.MembersRead);
        var before = (await MembersAsync(scenario)).Single(member => member.MembershipId == scenario.OwnerMembershipId);

        (await TransferAsync(scenario.Owner, scenario, recipient, await VersionAsync(scenario, recipient)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await MembersAsync(scenario)).Single(member => member.MembershipId == scenario.OwnerMembershipId);
        after.Status.ShouldBe(before.Status);
        after.RoleIds.ShouldBe(before.RoleIds);
        after.IsOwner.ShouldBeFalse();
    }

    /// <summary>
    /// Once the organization belongs to somebody else, the former owner's membership stops being protected — and
    /// that is the only way out of "the owner cannot be removed".
    /// </summary>
    [Test]
    public async Task Transferring_first_is_what_makes_the_former_owner_removable()
    {
        using var scenario = await OrganizationScenario.CreateAsync("ownership");
        var successor = await scenario.AddQuietMemberAsync("successor", Permissions.RolesManage, Permissions.MembersManage);

        var blocked = await StatusAsync(scenario, scenario.OwnerMembershipId, "revoke");
        blocked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await TransferAsync(scenario.Owner, scenario, successor, await VersionAsync(scenario, successor)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var removed = await StatusAsync(scenario, scenario.OwnerMembershipId, "revoke");

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> TransferAsync(Administrator actor, OrganizationScenario scenario, Guid toMembershipId, string version)
    {
        await actor.ProveAsync(ProofActions.OwnershipTransfer);
        return await TransferWithoutProvingAsync(actor, scenario, toMembershipId, version);
    }

    private static Task<HttpResponseMessage> TransferWithoutProvingAsync(Administrator actor, OrganizationScenario scenario, Guid toMembershipId, string version) =>
        actor.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/ownership/transfer", new { toMembershipId, version });

    private static async Task<HttpResponseMessage> StatusAsync(OrganizationScenario scenario, Guid membershipId, string change) =>
        await scenario.Owner.SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/{change}",
            new { version = await VersionAsync(scenario, membershipId) });

    private static async Task<MemberRow[]> MembersAsync(OrganizationScenario scenario) =>
        (await scenario.Owner.ReadAsync<MemberPageRow>($"/api/tenants/{scenario.TenantId.Value}/members")).Items;

    private static async Task<string> VersionAsync(OrganizationScenario scenario, Guid membershipId) =>
        (await MembersAsync(scenario)).Single(member => member.MembershipId == membershipId).Version;

    private sealed record MemberRow(Guid MembershipId, Guid IdentityId, string DisplayName, string NormalizedEmail, string Status, Guid[] RoleIds, bool IsOwner, string Version);

    private sealed record MemberPageRow(MemberRow[] Items, string? NextCursor);
}
