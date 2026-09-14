using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.Common.Validation;
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

    [Test]
    public async Task Blank_and_oversized_role_names_are_field_validation_and_change_no_roles()
    {
        using var scenario = await OrganizationAsync();
        var before = await TestApp.CountAsync<Role>();
        var createPath = $"/api/tenants/{scenario.TenantId.Value}/roles";

        await scenario.Owner.ProveAsync(ProofActions.RoleChange);
        using var blank = await CreateRoleAsync(scenario.Owner, scenario, " ", [], prove: false);
        await AssertNameValidationAsync(blank, createPath, ValidationDetail(ValidationErrorCodes.Required));
        (await TestApp.CountAsync<Role>()).ShouldBe(before);

        using var validCreate = await CreateRoleAsync(scenario.Owner, scenario, "Proof survives validation", [], prove: false);
        validCreate.StatusCode.ShouldBe(HttpStatusCode.Created,
            "field validation must not spend the proof needed by the valid follow-up");
        var created = (await validCreate.Content.ReadFromJsonAsync<RoleRow>())!;
        (await TestApp.CountAsync<Role>()).ShouldBe(before + 1);

        using var createOversized = await CreateRoleAsync(scenario.Owner, scenario, new string('r', 129), []);
        await AssertNameValidationAsync(
            createOversized, createPath, ValidationDetail(ValidationErrorCodes.TooLong, 128), new string('r', 129));
        (await TestApp.CountAsync<Role>()).ShouldBe(before + 1);

        var updatePath = $"/api/tenants/{scenario.TenantId.Value}/roles/{created.RoleId}";
        await scenario.Owner.ProveAsync(ProofActions.RoleChange);
        using var updateBlank = await UpdateRoleAsync(
            scenario.Owner, scenario, created.RoleId, " ", created.Permissions, created.Version, prove: false);
        await AssertNameValidationAsync(updateBlank, updatePath, ValidationDetail(ValidationErrorCodes.Required));

        using var validUpdate = await UpdateRoleAsync(
            scenario.Owner, scenario, created.RoleId, "Proof reused after validation", created.Permissions, created.Version, prove: false);
        validUpdate.StatusCode.ShouldBe(HttpStatusCode.OK,
            "field validation must not spend the proof needed by the valid follow-up");
        var updated = (await validUpdate.Content.ReadFromJsonAsync<RoleRow>())!;

        using var oversized = await UpdateRoleAsync(
            scenario.Owner, scenario, updated.RoleId, new string('r', 129), updated.Permissions, updated.Version);
        await AssertNameValidationAsync(
            oversized, updatePath, ValidationDetail(ValidationErrorCodes.TooLong, 128), new string('r', 129));

        (await TestApp.CountAsync<Role>()).ShouldBe(before + 1, "invalid names must reach no role write");
        (await GetRoleAsync(scenario.Owner, scenario, updated.RoleId)).Name.ShouldBe("Proof reused after validation");
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

    [Test]
    public async Task A_role_mutation_route_tenant_mismatch_keeps_the_existing_operation_refusal()
    {
        using var scenario = await OrganizationAsync();
        using var elsewhere = await OrganizationAsync();
        var before = await TestApp.CountAsync<Role>();
        await scenario.Owner.ProveAsync(ProofActions.RoleChange);

        using var response = await scenario.Owner.SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{elsewhere.TenantId.Value}/roles",
            new { name = "Wrong scope", permissions = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await IdentityHttpHarness.ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().ShouldBe("invalid_role_operation");
        problem.TryGetProperty("errors", out _).ShouldBeFalse();
        (await TestApp.CountAsync<Role>()).ShouldBe(before);
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

    /// <summary>
    /// A role list is one offset page: the page asked for, with the metadata a caller needs to reach the rest. A page
    /// size outside 1–100 is clamped rather than refused, because it is a client bug and not a validation failure.
    /// </summary>
    [Test]
    public async Task A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size()
    {
        using var scenario = await OrganizationAsync();
        await SeedRolesAsync(scenario, 29);
        var path = $"/api/tenants/{scenario.TenantId.Value}/roles";

        var second = await scenario.Owner.ReadAsync<System.Text.Json.JsonElement>($"{path}?pageNumber=2&pageSize=25");
        PageMember(second, "totalCount").GetInt32().ShouldBe(30, "the system Owner role plus the 29 seeded ones");
        PageMember(second, "pageNumber").GetInt32().ShouldBe(2);
        PageMember(second, "pageSize").GetInt32().ShouldBe(25);
        second.GetProperty("items").GetArrayLength().ShouldBe(5);

        var zero = await scenario.Owner.ReadAsync<System.Text.Json.JsonElement>($"{path}?pageSize=0");
        PageMember(zero, "pageSize").GetInt32().ShouldBe(1, "a page size of zero or less means one row");
        zero.GetProperty("items").GetArrayLength().ShouldBe(1);

        var huge = await scenario.Owner.ReadAsync<System.Text.Json.JsonElement>($"{path}?pageSize=500");
        PageMember(huge, "pageSize").GetInt32().ShouldBe(100, "a page size above the bound is clamped, never a validation failure");
        huge.GetProperty("items").GetArrayLength().ShouldBe(30);
    }

    [Test]
    public async Task Read_scope_mismatches_are_native_absence_for_real_and_random_tenants()
    {
        using var scenario = await OrganizationAsync();
        using var elsewhere = await OrganizationAsync();
        var roleId = (await ListRolesAsync(elsewhere.Owner, elsewhere)).Single().RoleId;

        foreach (var tenantId in new[] { elsewhere.TenantId.Value, Guid.NewGuid() })
        {
            foreach (var path in new[]
            {
                $"/api/tenants/{tenantId}/permission-catalog",
                $"/api/tenants/{tenantId}/roles?pageNumber=1&pageSize=25",
                $"/api/tenants/{tenantId}/roles/{roleId}"
            })
            {
                using var refused = await scenario.Owner.GetAsync(path);
                refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                var problem = await IdentityHttpHarness.ReadProblemAsync(refused);
                problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
                problem.GetProperty("code").GetString().ShouldBe("not_found");
                problem.GetProperty("detail").GetString().ShouldBe("That role is not available.");
            }
        }
    }

    private static async Task<OrganizationScenario> OrganizationAsync() => await OrganizationScenario.CreateAsync("roles");

    private static async Task SeedRolesAsync(OrganizationScenario scenario, int count)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == scenario.TenantId);
        for (var index = 0; index < count; index++)
        {
            context.TenantRoles.Add(Role.Create(tenant, $"Seeded role {index:D2}"));
        }

        await context.SaveChangesAsync();
    }

    private static System.Text.Json.JsonElement PageMember(System.Text.Json.JsonElement page, string name)
    {
        page.TryGetProperty(name, out var value).ShouldBeTrue($"the role page must carry {name}");
        return value;
    }

    private sealed record RoleRow(Guid RoleId, string Name, bool IsSystem, bool IsRetired, string[] Permissions, string Version);

    private sealed record RolePageRow(RoleRow[] Items);

    private sealed record CatalogRow(string Code, bool Grantable);

    /// <summary>The three role writes, each buying the single-use proof amendment D2 requires.</summary>
    private static async Task<HttpResponseMessage> CreateRoleAsync(Administrator actor, OrganizationScenario scenario, string name, string[] permissions, bool prove = true)
    {
        if (prove) await actor.ProveAsync(ProofActions.RoleChange);
        return await actor.SendAsync(HttpMethod.Post, $"/api/tenants/{scenario.TenantId.Value}/roles", new { name, permissions });
    }

    private static async Task<HttpResponseMessage> UpdateRoleAsync(Administrator actor, OrganizationScenario scenario, Guid roleId, string name, IReadOnlyList<string> permissions, string version, bool prove = true)
    {
        if (prove) await actor.ProveAsync(ProofActions.RoleChange);
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

    private static async Task AssertNameValidationAsync(
        HttpResponseMessage response,
        string instance,
        ValidationErrorDetail expectedDetail,
        params string[] submittedValues)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(raw);
        var problem = document.RootElement;
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
        problem.GetProperty("title").GetString().ShouldBe("Bad Request");
        problem.GetProperty("instance").GetString().ShouldBe(instance);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Select(property => property.Name).ShouldBe(["name"]);
        var details = errors.GetProperty("name").EnumerateArray().ToArray();
        details.Length.ShouldBe(1);
        var detail = details[0];
        detail.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
        detail.EnumerateObject().Select(property => property.Name).ShouldBe(["code", "params"]);
        detail.GetProperty("code").GetString().ShouldBe(expectedDetail.Code);
        var parameters = detail.GetProperty("params");
        parameters.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
        parameters.EnumerateObject().Select(property => property.Name).ShouldBe(expectedDetail.Params.Keys);
        foreach (var expectedParameter in expectedDetail.Params)
        {
            parameters.GetProperty(expectedParameter.Key).GetInt32().ShouldBe(expectedParameter.Value);
        }
        foreach (var value in submittedValues) raw.ShouldNotContain(value);
    }

    private static ValidationErrorDetail ValidationDetail(string code, int? max = null) =>
        new(code, max is null
            ? new Dictionary<string, int>()
            : new Dictionary<string, int> { ["max"] = max.Value });
}
