using System.Net;
using System.Net.Http.Json;
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
        var admin = scenario.Owner;

        var created = await admin.CreateRoleAsync("Bookkeeper", [Permissions.MembersRead]);

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

        var refused = await limited.CreateRoleAsync("Overreach", [Permissions.MembersManage]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_role_operation");
        (await TestApp.ListAsync<Role>()).ShouldNotContain(role => role.Name == "Overreach");
    }

    [Test]
    public async Task The_catalogue_says_which_codes_this_caller_could_actually_grant()
    {
        using var scenario = await OrganizationAsync();
        var limited = await scenario.AddMemberAsync("reader", Permissions.RolesManage, Permissions.RolesRead);

        var owner = await scenario.Owner.CatalogAsync();
        var narrow = await limited.CatalogAsync();

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
        var created = await scenario.Owner.CreateRoleAsync("Wide", [Permissions.MembersManage, Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        var limited = await scenario.AddMemberAsync("narrower", Permissions.RolesManage, Permissions.RolesRead, Permissions.MembersRead);

        // Narrowing is not granting. Refusing it would let one administrator's role permanently outrank another's.
        var narrowed = await limited.UpdateRoleAsync(role.RoleId, "Wide", [Permissions.MembersRead], role.Version);

        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK, await narrowed.Content.ReadAsStringAsync());
        (await narrowed.Content.ReadFromJsonAsync<RoleRow>())!.Permissions.ShouldBe([Permissions.MembersRead]);
    }

    [Test]
    public async Task A_system_role_is_not_editable_and_not_retirable()
    {
        using var scenario = await OrganizationAsync();
        var owner = (await scenario.Owner.ListRolesAsync()).Single(role => role.IsSystem);

        var edited = await scenario.Owner.UpdateRoleAsync(owner.RoleId, "Renamed", owner.Permissions, owner.Version);
        var retired = await scenario.Owner.RetireRoleAsync(owner.RoleId, owner.Version);

        edited.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        retired.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await scenario.Owner.ListRolesAsync()).Single(role => role.IsSystem).Name.ShouldBe("Owner");
    }

    [Test]
    public async Task A_stale_version_is_refused_and_changes_nothing()
    {
        using var scenario = await OrganizationAsync();
        var created = await scenario.Owner.CreateRoleAsync("Drifting", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        (await scenario.Owner.UpdateRoleAsync(role.RoleId, "Moved", [Permissions.MembersRead], role.Version)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await scenario.Owner.UpdateRoleAsync(role.RoleId, "Later", [], role.Version);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(stale)).GetProperty("code").GetString().ShouldBe("role_concurrency_conflict");
        (await scenario.Owner.GetRoleAsync(role.RoleId)).Name.ShouldBe("Moved");
    }

    [Test]
    public async Task Another_tenants_role_is_absent_rather_than_forbidden()
    {
        using var scenario = await OrganizationAsync();
        using var elsewhere = await OrganizationAsync();
        var theirs = (await elsewhere.Owner.ListRolesAsync()).Single();

        var response = await scenario.Owner.GetRoleResponseAsync(theirs.RoleId);

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
        var role = (await second.ListRolesAsync()).Single(candidate => candidate.Name == "deputy-role");
        var refused = await second.UpdateRoleAsync(role.RoleId, "deputy-role", [Permissions.RolesRead], role.Version);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("last_administrator_required");
        (await second.GetRoleAsync(role.RoleId)).Permissions
            .ShouldBe([Permissions.MembersManage, Permissions.RolesManage, Permissions.RolesRead], ignoreOrder: true,
                customMessage: "a refused change must leave the role exactly as it was");
    }

    [Test]
    public async Task Every_role_write_spends_a_proof_and_refuses_without_one()
    {
        using var scenario = await OrganizationAsync();

        var unproved = await scenario.Owner.CreateRoleAsync("Unproved", [], prove: false);

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
        var created = await scenario.Owner.CreateRoleAsync("Offered", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        var widened = await scenario.Owner.UpdateRoleAsync(role.RoleId, "Offered", [Permissions.MembersRead, Permissions.MembersManage], role.Version);

        widened.StatusCode.ShouldBe(HttpStatusCode.OK, await widened.Content.ReadAsStringAsync());
        (await TestApp.ListAsync<Invitation>()).Single().Status.ShouldBe(InvitationStatus.Cancelled);
        var audited = (await TestApp.ListAsync<AuditEvent>()).Where(entry => entry.EventType == "invitation.cancelled").ToArray();
        audited.ShouldHaveSingleItem().Metadata["outcome"].ShouldBe("role-widened");
    }

    [Test]
    public async Task Narrowing_a_role_leaves_the_offers_that_named_it_standing()
    {
        using var scenario = await OrganizationAsync();
        var created = await scenario.Owner.CreateRoleAsync("Steady", [Permissions.MembersRead, Permissions.MembersManage]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        var narrowed = await scenario.Owner.UpdateRoleAsync(role.RoleId, "Steady", [Permissions.MembersRead], role.Version);

        narrowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TestApp.ListAsync<Invitation>()).Single().Status
            .ShouldBe(InvitationStatus.Pending, "nobody was offered more than they were offered before");
    }

    [Test]
    public async Task Retiring_a_role_withdraws_its_offers_and_is_idempotent()
    {
        using var scenario = await OrganizationAsync();
        var created = await scenario.Owner.CreateRoleAsync("Temporary", [Permissions.MembersRead]);
        var role = (await created.Content.ReadFromJsonAsync<RoleRow>())!;
        await scenario.InviteAsync($"invitee-{Guid.NewGuid():N}@example.test", role.RoleId);

        (await scenario.Owner.RetireRoleAsync(role.RoleId, role.Version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var current = await scenario.Owner.GetRoleAsync(role.RoleId);
        var again = await scenario.Owner.RetireRoleAsync(role.RoleId, current.Version);

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent, "asking for a state that already holds is not a conflict");
        current.IsRetired.ShouldBeTrue();
        (await TestApp.ListAsync<Invitation>()).Single().Status.ShouldBe(InvitationStatus.Cancelled);
    }

    [Test]
    public async Task A_retired_role_grants_nothing_on_the_very_next_request()
    {
        using var scenario = await OrganizationAsync();
        var member = await scenario.AddMemberAsync("temp", Permissions.RolesRead);
        var role = (await scenario.Owner.ListRolesAsync()).Single(candidate => candidate.Name == "temp-role");

        (await scenario.Owner.RetireRoleAsync(role.RoleId, role.Version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await member.ListRolesResponseAsync()).StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "the permission is gone on the next request, not at some later refresh");
    }

    [Test]
    public async Task A_personal_context_administers_no_roles()
    {
        using var scenario = await OrganizationAsync();

        var refused = await scenario.Owner.ListRolesResponseAsync(TenantId.From(Guid.NewGuid()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "a route naming a tenant the session is not operating in is refused, never honoured");
    }

    private static async Task<RoleScenario> OrganizationAsync() => await RoleScenario.CreateAsync();

    private sealed record RoleRow(Guid RoleId, string Name, bool IsSystem, bool IsRetired, string[] Permissions, string Version);

    private sealed record RolePageRow(RoleRow[] Items, string? NextCursor);

    private sealed record CatalogRow(string Code, bool Grantable);

    /// <summary>
    /// One organization with a real owner, and the ability to add members holding exactly the codes a case needs.
    /// The owner's authority comes from the provisioner, not from a fixture, so the ceiling is tested against the
    /// authority a registration actually produces.
    /// </summary>
    private sealed class RoleScenario : IDisposable
    {
        private readonly IdentityHttpHarness.ProductionHarness _harness;
        private readonly string _host;

        private RoleScenario(IdentityHttpHarness.ProductionHarness harness, string host, TenantId tenantId, Guid ownerId, Administrator owner)
        {
            _harness = harness;
            _host = host;
            TenantId = tenantId;
            OwnerId = ownerId;
            Owner = owner;
        }

        internal TenantId TenantId { get; }

        internal Guid OwnerId { get; }

        internal Administrator Owner { get; }

        internal static async Task<RoleScenario> CreateAsync()
        {
            await IdentityHttpHarness.SeedPermissionCatalogAsync();
            var harness = IdentityHttpHarness.CreateProductionHarness();
            var host = Host();
            var email = $"owner-{Guid.NewGuid():N}@example.test";
            var ownerId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
            var tenantId = await SeedOwnerTenantAsync(ownerId);
            var owner = await Administrator.SignInAsync(harness, host, email, tenantId);
            return new RoleScenario(harness, host, tenantId, ownerId, owner);
        }

        /// <summary>
        /// An organization owned the way `RegistrationInitialRoleProvisioner` owns one: the system role, the
        /// assignment, and the codes amendment D1 says the owner holds.
        /// </summary>
        private static async Task<TenantId> SeedOwnerTenantAsync(Guid ownerId)
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = Tenant.CreateOrganization(TenantSlug.From($"roles-{Guid.NewGuid():N}"));
            tenant.Activate();
            var membership = TenantMembership.CreateResponsible(tenant, ownerId);
            membership.Activate(tenant);
            var role = Role.CreateSystem(tenant, "Owner");
            context.AddRange(tenant, membership, role, MembershipRole.Create(tenant, membership, role));
            foreach (var permission in await context.Permissions.Where(candidate => Permissions.OrganizationOwnerCodes.Contains(candidate.Code)).ToListAsync())
            {
                context.Add(RolePermission.Create(tenant, role, permission));
            }

            await context.SaveChangesAsync();
            return tenant.Id;
        }

        internal async Task<Administrator> AddMemberAsync(string label, params string[] codes)
        {
            var email = $"{label}-{Guid.NewGuid():N}@example.test";
            var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);

            using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == TenantId);
                var membership = TenantMembership.CreateResponsible(tenant, identityId);
                membership.Activate(tenant);
                var role = Role.Create(tenant, $"{label}-role");
                context.AddRange(membership, role, MembershipRole.Create(tenant, membership, role));
                foreach (var permission in await context.Permissions.Where(candidate => codes.Contains(candidate.Code)).ToListAsync())
                {
                    context.Add(RolePermission.Create(tenant, role, permission));
                }

                await context.SaveChangesAsync();
            }

            return await Administrator.SignInAsync(_harness, _host, email, TenantId);
        }

        /// <summary>Takes the owner's own administration away, so a custom role becomes the only source of it.</summary>
        internal async Task RetireOwnerMembershipRoleAsync()
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var membership = await context.TenantMemberships.SingleAsync(candidate => candidate.TenantId == TenantId && candidate.IdentityId == OwnerId);
            var links = await context.MembershipRoles.Where(link => link.TenantId == TenantId && link.MembershipId == membership.Id).ToListAsync();
            context.MembershipRoles.RemoveRange(links);
            await context.SaveChangesAsync();
        }

        internal async Task InviteAsync(string email, Guid roleId)
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == TenantId);
            var role = await context.TenantRoles.SingleAsync(candidate => candidate.TenantId == TenantId && candidate.Id == RoleId.From(roleId));
            var invitation = Invitation.Issue(
                tenant,
                email,
                [role],
                CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash.Of($"token-{Guid.NewGuid():N}"),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(7));
            context.Add(invitation);
            await context.SaveChangesAsync();
        }

        public void Dispose() => _harness.Dispose();
    }

    /// <summary>One signed-in administrator, carrying its own cookie and buying its own proofs.</summary>
    private sealed class Administrator(HttpClient client, string host, string cookie, TenantId tenantId)
    {
        internal static async Task<Administrator> SignInAsync(IdentityHttpHarness.ProductionHarness harness, string host, string email, TenantId tenantId)
        {
            var client = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = false,
                AllowAutoRedirect = false
            });

            var antiforgery = await AntiforgeryAsync(client, host, null);
            using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = Password }, antiforgery.Token);
            request.Headers.Add("Cookie", antiforgery.Cookie);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
            var cookie = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))
                .Split(';')[0];
            return new Administrator(client, host, cookie, tenantId);
        }

        internal async Task<HttpResponseMessage> CreateRoleAsync(string name, string[] permissions, bool prove = true)
        {
            if (prove) await ProveAsync(ProofActions.RoleChange);
            return await SendAsync(HttpMethod.Post, $"/api/tenants/{tenantId.Value}/roles", new { name, permissions });
        }

        internal async Task<HttpResponseMessage> UpdateRoleAsync(Guid roleId, string name, IReadOnlyList<string> permissions, string version)
        {
            await ProveAsync(ProofActions.RoleChange);
            return await SendAsync(HttpMethod.Put, $"/api/tenants/{tenantId.Value}/roles/{roleId}", new { name, permissions, version });
        }

        internal async Task<HttpResponseMessage> RetireRoleAsync(Guid roleId, string version)
        {
            await ProveAsync(ProofActions.RoleChange);
            return await SendAsync(HttpMethod.Post, $"/api/tenants/{tenantId.Value}/roles/{roleId}/retire", new { version });
        }

        internal async Task<CatalogRow[]> CatalogAsync()
        {
            var response = await GetAsync($"/api/tenants/{tenantId.Value}/permission-catalog");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<CatalogRow[]>())!;
        }

        internal async Task<RoleRow[]> ListRolesAsync()
        {
            var response = await ListRolesResponseAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<RolePageRow>())!.Items;
        }

        internal Task<HttpResponseMessage> ListRolesResponseAsync(TenantId? other = null) =>
            GetAsync($"/api/tenants/{(other ?? tenantId).Value}/roles");

        internal async Task<RoleRow> GetRoleAsync(Guid roleId)
        {
            var response = await GetRoleResponseAsync(roleId);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<RoleRow>())!;
        }

        internal Task<HttpResponseMessage> GetRoleResponseAsync(Guid roleId) =>
            GetAsync($"/api/tenants/{tenantId.Value}/roles/{roleId}");

        private async Task ProveAsync(string action)
        {
            var antiforgery = await AntiforgeryAsync(client, host, cookie);
            using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/reauthenticate", new { action, password = Password }, antiforgery.Token);
            request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
        {
            var antiforgery = await AntiforgeryAsync(client, host, cookie);
            using var request = IdentityHttpHarness.JsonRequest(method, $"{host}{path}", body, antiforgery.Token);
            request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
            return await client.SendAsync(request);
        }

        private async Task<HttpResponseMessage> GetAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}{path}");
            request.Headers.Add("Cookie", cookie);
            return await client.SendAsync(request);
        }

        private static async Task<(string Cookie, string Token)> AntiforgeryAsync(HttpClient client, string host, string? cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
            var pair = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal))
                .Split(';')[0];
            return (pair, token);
        }
    }
}
