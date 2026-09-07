using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>
/// Member administration inside an `Organization` (IA-REQ-053, amendments D2 and D4).
/// <para>
/// Everything runs over the production HTTP pipeline, because what is being tested is what an administrator
/// holding a cookie can and cannot cause to somebody else. The two rules that shape it are the same ones roles
/// have — the grant ceiling and the administrator floor — plus one that only exists here: an organization whose
/// owner is not a member of it has nobody who can give it away.
/// </para>
/// </summary>
public sealed class MembershipAdministrationTests : TestBase
{
    [Test]
    public async Task The_member_list_says_who_is_in_the_organization_and_which_one_owns_it()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);

        var page = await scenario.Owner.ReadAsync<MemberPageRow>($"/api/tenants/{scenario.TenantId.Value}/members");

        page.Items.Length.ShouldBe(2);
        var owner = page.Items.Single(member => member.IsOwner);
        owner.MembershipId.ShouldBe(scenario.OwnerMembershipId);
        owner.Status.ShouldBe(nameof(MembershipStatus.Active));
        owner.RoleIds.Length.ShouldBe(1);
        page.Items.ShouldAllBe(member => member.Version.Length > 0);
    }

    /// <summary>
    /// Amendment D4. This is the first `Organization` route that returns another person's identifying data, so
    /// what it returns is asserted rather than assumed — and what it does not return with it.
    /// </summary>
    [Test]
    public async Task A_member_row_carries_a_name_and_an_address_and_nothing_else_about_the_person()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);

        var response = await scenario.Owner.GetAsync($"/api/tenants/{scenario.TenantId.Value}/members");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("@example.test", Case.Insensitive);
        body.Contains(OrganizationScenario.Password, StringComparison.Ordinal).ShouldBeFalse();
        System.Text.Json.JsonDocument.Parse(body).RootElement
            .GetProperty("items")[0].EnumerateObject().Select(member => member.Name)
            .ShouldBe(["membershipId", "identityId", "displayName", "normalizedEmail", "status", "roleIds", "isOwner", "version"], ignoreOrder: true);
    }

    [Test]
    public async Task An_administrator_changes_which_roles_a_member_holds()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var role = await CreateRoleAsync(scenario, "Auditor", Permissions.MembersRead);

        var changed = await AssignAsync(scenario.Owner, scenario, membershipId, [role], await VersionAsync(scenario, membershipId));

        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var member = await ReadMemberAsync(scenario, membershipId);
        member.RoleIds.ShouldBe([role]);
    }

    /// <summary>
    /// The ceiling, in the form assignment takes it: handing somebody a role hands them everything in it, so it is
    /// the whole set that is compared against what the actor holds, not the difference.
    /// </summary>
    [Test]
    public async Task Nobody_can_hand_out_a_role_conferring_more_than_they_hold_themselves()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        // `Wide` confers role administration, which the actor does not hold. Everything else in it they do hold,
        // so only the whole-set comparison can refuse this — a difference-based one would let it through.
        var wide = await CreateRoleAsync(scenario, "Wide", Permissions.RolesManage, Permissions.MembersRead);
        var limited = await scenario.AddMemberAsync("limited", Permissions.MembersManage, Permissions.MembersRead);

        var refused = await AssignAsync(limited.Acting, scenario, membershipId, [wide], await VersionAsync(scenario, membershipId));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_membership_operation");
        (await ReadMemberAsync(scenario, membershipId)).RoleIds.ShouldNotContain(wide);
    }

    [Test]
    public async Task Assigning_roles_spends_a_proof_and_is_refused_without_one()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var role = await CreateRoleAsync(scenario, "Auditor", Permissions.MembersRead);

        var unproved = await scenario.Owner.SendAsync(
            HttpMethod.Put,
            $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/roles",
            new { roleIds = new[] { role }, version = await VersionAsync(scenario, membershipId) });

        unproved.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(unproved)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await ReadMemberAsync(scenario, membershipId)).RoleIds.ShouldNotContain(role);
    }

    [Test]
    public async Task A_stale_version_changes_nothing()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var role = await CreateRoleAsync(scenario, "Auditor", Permissions.MembersRead);
        var stale = await VersionAsync(scenario, membershipId);
        (await SuspendAsync(scenario.Owner, scenario, membershipId, stale)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refused = await AssignAsync(scenario.Owner, scenario, membershipId, [role], stale);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("membership_concurrency_conflict");
        (await ReadMemberAsync(scenario, membershipId)).RoleIds.ShouldNotContain(role);
    }

    [Test]
    public async Task A_suspended_member_can_be_reactivated_and_is_audited_as_what_happened()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);

        (await SuspendAsync(scenario.Owner, scenario, membershipId, await VersionAsync(scenario, membershipId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadMemberAsync(scenario, membershipId)).Status.ShouldBe(nameof(MembershipStatus.Suspended));
        (await StatusAsync(scenario.Owner, scenario, membershipId, "reactivate", await VersionAsync(scenario, membershipId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadMemberAsync(scenario, membershipId)).Status.ShouldBe(nameof(MembershipStatus.Active));
        var outcomes = (await TestApp.ListAsync<AuditEvent>())
            .Where(entry => entry.EventType == "membership.changed" && entry.ActorId == scenario.OwnerId)
            .Select(entry => entry.Metadata["outcome"])
            .ToArray();
        outcomes.ShouldBe(["suspended", "reactivated"], ignoreOrder: true);
    }

    [Test]
    public async Task Asking_for_the_state_a_member_is_already_in_answers_the_same_and_records_nothing_new()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        (await SuspendAsync(scenario.Owner, scenario, membershipId, await VersionAsync(scenario, membershipId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await SuspendAsync(scenario.Owner, scenario, membershipId, await VersionAsync(scenario, membershipId));

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "membership.changed" && entry.Metadata["outcome"] == "suspended")
            .ShouldBe(1, "a retry is the caller asking for a state that already holds");
    }

    /// <summary>
    /// Revoking ends the membership, so the authority it carried goes with it. There is no way back to `Active`
    /// from here: a returning member comes through a fresh invitation, which is a different act.
    /// </summary>
    [Test]
    public async Task Revoking_a_member_removes_what_they_held_and_cannot_be_undone_by_reactivating()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);

        (await StatusAsync(scenario.Owner, scenario, membershipId, "revoke", await VersionAsync(scenario, membershipId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var refused = await StatusAsync(scenario.Owner, scenario, membershipId, "reactivate", await VersionAsync(scenario, membershipId));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_membership_operation");
        var member = await ReadMemberAsync(scenario, membershipId);
        member.Status.ShouldBe(nameof(MembershipStatus.Revoked));
        member.RoleIds.ShouldBeEmpty("what they held went with the membership");
    }

    /// <summary>
    /// The rule only membership has. Suspending or revoking the owner would leave an organization nobody can give
    /// away, so the way out is to transfer first (IA-REQ-053).
    /// </summary>
    [TestCase("suspend")]
    [TestCase("revoke")]
    public async Task The_owners_own_membership_cannot_be_ended(string change)
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");

        var refused = await StatusAsync(scenario.Owner, scenario, scenario.OwnerMembershipId, change, await VersionAsync(scenario, scenario.OwnerMembershipId));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_membership_operation");
        (await ReadMemberAsync(scenario, scenario.OwnerMembershipId)).Status.ShouldBe(nameof(MembershipStatus.Active));
    }

    [Test]
    public async Task A_change_that_would_leave_nobody_able_to_administer_is_refused()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var spare = await scenario.AddQuietMemberAsync("deputy", Permissions.RolesManage, Permissions.MembersManage);
        var acting = await scenario.AddMemberAsync("acting", Permissions.MembersManage, Permissions.RolesManage, Permissions.MembersRead);
        await scenario.RetireOwnerMembershipRoleAsync();

        // Two administrators remain, so suspending one is allowed. The floor refuses only the change that would
        // reach zero — which is the acting administrator taking their own roles away.
        (await SuspendAsync(acting.Acting, scenario, spare, await VersionAsync(acting.Acting, scenario, spare))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var refused = await AssignAsync(acting.Acting, scenario, acting.MembershipId, [], await VersionAsync(acting.Acting, scenario, acting.MembershipId));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("last_administrator_required");
        (await ReadMemberAsync(acting.Acting, scenario, acting.MembershipId)).RoleIds
            .ShouldNotBeEmpty("a refused change leaves the member exactly as they were");
    }

    [Test]
    public async Task The_invitation_list_shows_the_offer_and_never_anything_that_could_accept_it()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        var role = await CreateRoleAsync(scenario, "Offered", Permissions.MembersRead);
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role);

        var response = await scenario.Owner.GetAsync($"/api/tenants/{scenario.TenantId.Value}/invitations");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        System.Text.Json.JsonDocument.Parse(body).RootElement
            .GetProperty("items")[0].EnumerateObject().Select(member => member.Name)
            .ShouldBe(["invitationId", "normalizedEmail", "status", "createdAt", "expiresAt", "roleIds"], ignoreOrder: true);
        body.Contains("token", StringComparison.OrdinalIgnoreCase).ShouldBeFalse("nothing here may be used to accept");
        body.Contains("Hash", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Test]
    public async Task A_route_naming_another_tenant_is_refused_rather_than_honoured()
    {
        using var scenario = await OrganizationScenario.CreateAsync("members");
        using var elsewhere = await OrganizationScenario.CreateAsync("elsewhere");

        var refused = await scenario.Owner.GetAsync($"/api/tenants/{elsewhere.TenantId.Value}/members");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "only the session decides which organization the caller is in");
    }

    private static async Task<Guid> CreateRoleAsync(OrganizationScenario scenario, string name, params string[] codes)
    {
        await scenario.Owner.ProveAsync(ProofActions.RoleChange);
        var created = await scenario.Owner.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/roles", new { name, permissions = codes });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<RoleRow>())!.RoleId;
    }

    private static async Task<HttpResponseMessage> AssignAsync(Administrator actor, OrganizationScenario scenario, Guid membershipId, Guid[] roleIds, string version)
    {
        await actor.ProveAsync(ProofActions.MemberRoleChange);
        return await actor.SendAsync(HttpMethod.Put, $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/roles", new { roleIds, version });
    }

    private static Task<HttpResponseMessage> SuspendAsync(Administrator actor, OrganizationScenario scenario, Guid membershipId, string version) =>
        StatusAsync(actor, scenario, membershipId, "suspend", version);

    private static Task<HttpResponseMessage> StatusAsync(Administrator actor, OrganizationScenario scenario, Guid membershipId, string change, string version) =>
        actor.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/{change}", new { version });

    private static Task<MemberRow> ReadMemberAsync(OrganizationScenario scenario, Guid membershipId) =>
        ReadMemberAsync(scenario.Owner, scenario, membershipId);

    /// <summary>
    /// Who does the reading matters once a case has taken the owner's own administration away: reading a list
    /// needs `members.read` like anything else, so a case that retires the owner's role must read as somebody who
    /// still holds one.
    /// </summary>
    private static async Task<MemberRow> ReadMemberAsync(Administrator reader, OrganizationScenario scenario, Guid membershipId) =>
        (await reader.ReadAsync<MemberPageRow>($"/api/tenants/{scenario.TenantId.Value}/members"))
        .Items.Single(member => member.MembershipId == membershipId);

    private static Task<string> VersionAsync(OrganizationScenario scenario, Guid membershipId) =>
        VersionAsync(scenario.Owner, scenario, membershipId);

    private static async Task<string> VersionAsync(Administrator reader, OrganizationScenario scenario, Guid membershipId) =>
        (await ReadMemberAsync(reader, scenario, membershipId)).Version;

    private sealed record MemberRow(Guid MembershipId, Guid IdentityId, string DisplayName, string NormalizedEmail, string Status, Guid[] RoleIds, bool IsOwner, string Version);

    private sealed record MemberPageRow(MemberRow[] Items, string? NextCursor);

    private sealed record RoleRow(Guid RoleId, string Name, bool IsSystem, bool IsRetired, string[] Permissions, string Version);
}
