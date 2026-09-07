using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Roles;

/// <summary>
/// Custom roles inside an `Organization` (IA-REQ-053, amendments D1–D3).
/// <para>
/// Everything runs over the production HTTP pipeline, because what is being tested is what an administrator
/// holding a cookie can and cannot cause: the grant ceiling and the administrator floor are both statements about
/// authority, and authority is decided by the request, not by a method call.
/// </para>
/// </summary>
public sealed class RoleAdministrationTests : TestBase
{
    private const string Password = "Testing1234!";

    private static string Host() => $"https://roles-{Guid.NewGuid():N}.localhost";

    [Test]
    public async Task An_administrator_creates_a_role_and_it_confers_exactly_what_was_asked_for()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Bookkeeper", [Permissions.MembersRead]);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        created.Headers.Location!.ToString().ShouldContain($"/api/tenants/{scenario.TenantId.Value}/roles/");
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        role.Name.ShouldBe("Bookkeeper");
        role.IsSystem.ShouldBeFalse();
        role.Permissions.ShouldBe([Permissions.MembersRead]);
        role.Version.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The ceiling, which is the whole of C5's grant rule: what an actor may put into a role is bounded by what
    /// the actor itself effectively holds. Without it, `roles.manage` alone would be every permission there is.
    /// </summary>
    [Test]
    public async Task Nobody_can_put_a_permission_into_a_role_that_they_do_not_hold_themselves()
    {
        using var scenario = await OrganizationAsync();
        var limited = await scenario.AddMemberAsync("limited", Permissions.RolesManage, Permissions.RolesRead);

        var refused = await CreateRoleAsync(limited.Acting, scenario, "Overreach", [Permissions.MembersManage]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_role_operation");
        (await TestApp.ListAsync<Role>()).ShouldNotContain(role => role.Name == "Overreach");
    }

    [Test]
    public async Task The_catalogue_says_which_codes_this_caller_could_actually_grant()
    {
        using var scenario = await OrganizationAsync();
        var limited = await scenario.AddMemberAsync("reader", Permissions.RolesManage, Permissions.RolesRead);

        var owner = await CatalogAsync(scenario.Owner, scenario);
        var narrow = await CatalogAsync(limited.Acting, scenario);

        owner.ShouldAllBe(entry => entry.Grantable);
        narrow.Where(entry => entry.Grantable).Select(entry => entry.Code)
            .ShouldBe([Permissions.RolesManage, Permissions.RolesRead], ignoreOrder: true);
        narrow.Select(entry => entry.Code).ShouldBe(owner.Select(entry => entry.Code),
            "the catalogue is the same list for everybody; only what they may grant differs");
    }

    [Test]
    public async Task A_role_edit_may_remove_a_permission_the_editor_does_not_hold()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Wide", [Permissions.MembersManage, Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        var limited = await scenario.AddMemberAsync("narrower", Permissions.RolesManage, Permissions.RolesRead, Permissions.MembersRead);

        // Narrowing is not granting. Refusing it would let one administrator's role permanently outrank another's.
        var narrowed = await UpdateRoleAsync(limited.Acting, scenario, role.RoleId, "Wide", [Permissions.MembersRead], role.Version);

        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK, await narrowed.Content.ReadAsStringAsync());
        (await narrowed.Content.ReadFromJsonAsync<RoleRow>())!.Permissions.ShouldBe([Permissions.MembersRead]);
    }

    [Test]
    public async Task A_system_role_is_not_editable_and_not_retirable()
    {
        using var scenario = await OrganizationAsync();
        var owner = (await ListRolesAsync(scenario.Owner, scenario)).Single(role => role.IsSystem);

        var edited = await UpdateRoleAsync(scenario.Owner, scenario, owner.RoleId, "Renamed", owner.Permissions, owner.Version);
        var retired = await RetireRoleAsync(scenario.Owner, scenario, owner.RoleId, owner.Version);

        edited.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        retired.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ListRolesAsync(scenario.Owner, scenario)).Single(role => role.IsSystem).Name.ShouldBe("Owner");
    }

    [Test]
    public async Task A_stale_version_is_refused_and_changes_nothing()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Drifting", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        (await UpdateRoleAsync(scenario.Owner, scenario, role.RoleId, "Moved", [Permissions.MembersRead], role.Version)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await UpdateRoleAsync(scenario.Owner, scenario, role.RoleId, "Later", [], role.Version);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(stale)).GetProperty("code").GetString().ShouldBe("role_concurrency_conflict");
        (await GetRoleAsync(scenario.Owner, scenario, role.RoleId)).Name.ShouldBe("Moved");
    }

    [Test]
    public async Task Another_tenants_role_is_absent_rather_than_forbidden()
    {
        using var scenario = await OrganizationAsync();
        using var elsewhere = await OrganizationAsync();
        var theirs = (await ListRolesAsync(elsewhere.Owner, elsewhere)).Single();

        var response = await scenario.Owner.GetAsync($"/api/tenants/{scenario.TenantId.Value}/roles/" + theirs.RoleId);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "a 403 would confirm the identifier exists somewhere (IA-REQ-030)");
    }

    /// <summary>
    /// The floor. The owner's own role is protected from editing, so the way to reach zero administrators is to
    /// retire the custom role a second administrator depends on — and that is what must be refused.
    /// </summary>
    [Test]
    public async Task A_change_that_would_leave_nobody_able_to_administer_is_refused()
    {
        using var scenario = await OrganizationAsync();
        var second = await scenario.AddMemberAsync("deputy", Permissions.RolesManage, Permissions.MembersManage, Permissions.RolesRead);
        await scenario.RetireOwnerMembershipRoleAsync();

        // The deputy is now the only administrator, and their authority comes from the role they are about to
        // narrow. The floor is counted after the write, from flushed state.
        var role = (await ListRolesAsync(second.Acting, scenario)).Single(candidate => candidate.Name == "deputy-role");
        var refused = await UpdateRoleAsync(second.Acting, scenario, role.RoleId, "deputy-role", [Permissions.RolesRead], role.Version);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("last_administrator_required");
        (await GetRoleAsync(second.Acting, scenario, role.RoleId)).Permissions
            .ShouldBe([Permissions.MembersManage, Permissions.RolesManage, Permissions.RolesRead], ignoreOrder: true,
                customMessage: "a refused change must leave the role exactly as it was");
    }

    [Test]
    public async Task Every_role_write_spends_a_proof_and_refuses_without_one()
    {
        using var scenario = await OrganizationAsync();

        var unproved = await CreateRoleAsync(scenario.Owner, scenario, "Unproved", [], prove: false);

        unproved.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(unproved)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await TestApp.ListAsync<Role>()).ShouldNotContain(role => role.Name == "Unproved");
    }

    /// <summary>
    /// IA-REQ-047's closing rule, which C5 replaces: a widened role cannot reach acceptance. The offer is
    /// withdrawn in the same transaction as the widening, and the token in the mailbox stops working.
    /// </summary>
    [Test]
    public async Task Widening_a_role_withdraws_every_offer_that_named_it()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Offered", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        var widened = await UpdateRoleAsync(scenario.Owner, scenario, role.RoleId, "Offered", [Permissions.MembersRead, Permissions.MembersManage], role.Version);

        widened.StatusCode.ShouldBe(HttpStatusCode.OK, await widened.Content.ReadAsStringAsync());
        (await TestApp.ListAsync<Invitation>()).Single().Status.ShouldBe(InvitationStatus.Cancelled);
        var audited = (await TestApp.ListAsync<AuditEvent>()).Where(entry => entry.EventType == "invitation.cancelled").ToArray();
        audited.ShouldHaveSingleItem().Metadata["outcome"].ShouldBe("role-widened");
    }

    [Test]
    public async Task Narrowing_a_role_leaves_the_offers_that_named_it_standing()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Steady", [Permissions.MembersRead, Permissions.MembersManage]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        var narrowed = await UpdateRoleAsync(scenario.Owner, scenario, role.RoleId, "Steady", [Permissions.MembersRead], role.Version);

        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TestApp.ListAsync<Invitation>()).Single().Status
            .ShouldBe(InvitationStatus.Pending, "nobody was offered more than they were offered before");
    }

    [Test]
    public async Task Retiring_a_role_withdraws_its_offers_and_is_idempotent()
    {
        using var scenario = await OrganizationAsync();
        var created = await CreateRoleAsync(scenario.Owner, scenario, "Temporary", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        (await RetireRoleAsync(scenario.Owner, scenario, role.RoleId, role.Version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var current = await GetRoleAsync(scenario.Owner, scenario, role.RoleId);
        var again = await RetireRoleAsync(scenario.Owner, scenario, role.RoleId, current.Version);

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent, "asking for a state that already holds is not a conflict");
        current.IsRetired.ShouldBeTrue();
        (await TestApp.ListAsync<Invitation>()).Single().Status.ShouldBe(InvitationStatus.Cancelled);
    }

    [Test]
    public async Task A_retired_role_grants_nothing_on_the_very_next_request()
    {
        using var scenario = await OrganizationAsync();
        var member = await scenario.AddMemberAsync("temp", Permissions.RolesRead);
        var role = (await ListRolesAsync(scenario.Owner, scenario)).Single(candidate => candidate.Name == "temp-role");

        (await RetireRoleAsync(scenario.Owner, scenario, role.RoleId, role.Version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await member.Acting.GetAsync($"/api/tenants/{scenario.TenantId.Value}/roles")).StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "the permission is gone on the next request, not at some later refresh");
    }

    [Test]
    public async Task A_personal_context_administers_no_roles()
    {
        using var scenario = await OrganizationAsync();

        var refused = await scenario.Owner.GetAsync($"/api/tenants/{Guid.NewGuid()}/roles");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "a route naming a tenant the session is not operating in is refused, never honoured");
    }

    private static async Task<OrganizationScenario> OrganizationAsync() => await OrganizationScenario.CreateAsync("roles");

    private sealed record RoleRow(Guid RoleId, string Name, bool IsSystem, bool IsRetired, string[] Permissions, string Version);

    private sealed record RolePageRow(RoleRow[] Items, string? NextCursor);

    private sealed record CatalogRow(string Code, bool Grantable);

    /// <summary>The three role writes, each buying the single-use proof amendment D2 requires.</summary>
    private static async Task<HttpResponseMessage> CreateRoleAsync(Administrator actor, OrganizationScenario scenario, string name, string[] permissions, bool prove = true)
    {
        if (prove) await actor.ProveAsync(ProofActions.RoleChange);
        return await actor.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/roles", new { name, permissions });
    }

    private static async Task<HttpResponseMessage> UpdateRoleAsync(Administrator actor, OrganizationScenario scenario, Guid roleId, string name, IReadOnlyList<string> permissions, string version)
    {
        await actor.ProveAsync(ProofActions.RoleChange);
        return await actor.SendAsync(HttpMethod.Put, $"/api/tenants/{scenario.TenantId.Value}/roles/{roleId}", new { name, permissions, version });
    }

    private static async Task<HttpResponseMessage> RetireRoleAsync(Administrator actor, OrganizationScenario scenario, Guid roleId, string version)
    {
        await actor.ProveAsync(ProofActions.RoleChange);
        return await actor.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/roles/{roleId}/retire", new { version });
    }

    private static async Task<CatalogRow[]> CatalogAsync(Administrator actor, OrganizationScenario scenario) =>
        await actor.ReadAsync<CatalogRow[]>($"/api/tenants/{scenario.TenantId.Value}/permission-catalog");

    private static async Task<RoleRow[]> ListRolesAsync(Administrator actor, OrganizationScenario scenario) =>
        (await actor.ReadAsync<RolePageRow>($"/api/tenants/{scenario.TenantId.Value}/roles")).Items;

    private static async Task<RoleRow> GetRoleAsync(Administrator actor, OrganizationScenario scenario, Guid roleId) =>
        await actor.ReadAsync<RoleRow>($"/api/tenants/{scenario.TenantId.Value}/roles/{roleId}");
}
