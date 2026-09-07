using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>Directed reproductions of R3, R4 and R5 against accepted C5, with production HTTP authorization.</summary>
public sealed class DelegatedAdministrationRevalidationTests : TestBase
{
    [Test]
    public async Task R3_An_offer_paused_before_insert_cannot_grant_a_subsequently_widened_role()
    {
        using var organization = await OrganizationScenario.CreateAsync("r3");
        var role = await CreateRoleAsync(organization, "Offered", Permissions.MembersRead);
        var limited = await organization.AddMemberAsync("limited", Permissions.MembersInvite, Permissions.MembersRead);
        var recipientEmail = $"r3-recipient-{Guid.NewGuid():N}@example.test";
        var recipientId = await IdentityHttpHarness.SeedConfirmedUserAsync(recipientEmail, OrganizationScenario.Password);
        var barrier = new InvitationBeforeInsertBarrier(role.RoleId);
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
            services.AddSingleton<ISaveChangesInterceptor>(barrier));
        var host = $"https://r3-race-{Guid.NewGuid():N}.localhost";
        var inviterEmail = (await IdentityHttpHarness.GetUserAsync(limited.IdentityId)).Email!;
        var ownerEmail = (await IdentityHttpHarness.GetUserAsync(organization.OwnerId)).Email!;
        var inviter = await Administrator.SignInAsync(harness, host, inviterEmail, organization.TenantId);
        var owner = await Administrator.SignInAsync(harness, host, ownerEmail, organization.TenantId);
        var recipient = await Administrator.SignInAsync(harness, host, recipientEmail, organization.TenantId);
        await owner.ProveAsync(ProofActions.RoleChange);

        barrier.Arm();
        var issue = inviter.SendAsync(HttpMethod.Post, Invitations(organization), new { email = recipientEmail, roleIds = new[] { role.RoleId } });
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // The read/offer validation is complete, but no invitation exists in any committed transaction yet.
            (await TestApp.ListAsync<Invitation>()).ShouldBeEmpty();
            var widened = await owner.SendAsync(HttpMethod.Put, RolePath(organization, role.RoleId),
                new { name = role.Name, permissions = new[] { Permissions.MembersRead, Permissions.MembersManage }, version = role.Version });
            widened.StatusCode.ShouldBe(HttpStatusCode.OK, await widened.Content.ReadAsStringAsync());
            barrier.InvitationBackendPid.ShouldBeGreaterThan(0);
            barrier.WideningBackendPid.ShouldBeGreaterThan(0);
            barrier.WideningBackendPid.ShouldNotBe(barrier.InvitationBackendPid,
                "the barrier and widening must run on independent PostgreSQL connections and transactions");
            (await TestApp.ListAsync<Invitation>()).ShouldBeEmpty("the widening committed before the held INSERT");
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        using var issued = await issue.WaitAsync(TimeSpan.FromSeconds(20));
        issued.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        HttpStatusCode? acceptedStatus = null;
        if (issued.StatusCode == HttpStatusCode.Created)
        {
            var invitationId = (await issued.Content.ReadFromJsonAsync<IssuedRow>())!.InvitationId;
            var token = await InvitationTokenAsync(harness, invitationId);
            using var accepted = await recipient.SendAsync(HttpMethod.Post, "/api/invitations/accept", new { token });
            acceptedStatus = accepted.StatusCode;
            accepted.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        }

        var finalCodes = await EffectiveCodesAsync(recipientId, organization.TenantId);
        TestContext.Out.WriteLine($"R3 invitation PID={barrier.InvitationBackendPid}, widening PID={barrier.WideningBackendPid}; issue={(int)issued.StatusCode}; accept={acceptedStatus}; final permissions={string.Join(',', finalCodes)}");
        finalCodes.ShouldNotContain(Permissions.MembersManage,
            "the inviter held only members.invite and members.read; C5 forbids granting an unoffered permission at commit time");
    }

    [Test]
    public async Task R4A_A_sequential_permission_only_edit_rejects_the_original_role_version()
    {
        using var organization = await OrganizationScenario.CreateAsync("r4-role");
        var other = await organization.AddMemberAsync("other-admin", Permissions.RolesRead, Permissions.RolesManage,
            Permissions.MembersRead, Permissions.MembersManage, Permissions.MembersInvite);
        var original = await CreateRoleAsync(organization, "Stable name", Permissions.MembersRead);
        var secondView = await other.Acting.ReadAsync<RoleRow>(RolePath(organization, original.RoleId));
        secondView.Version.ShouldBe(original.Version);

        using var first = await UpdateRoleAsync(organization.Owner, organization, original,
            [Permissions.MembersRead, Permissions.MembersManage]);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var afterFirst = (await first.Content.ReadFromJsonAsync<RoleRow>())!;
        using var second = await UpdateRoleAsync(other.Acting, organization, secondView,
            [Permissions.MembersRead, Permissions.MembersInvite]);
        var final = await organization.Owner.ReadAsync<RoleRow>(RolePath(organization, original.RoleId));

        TestContext.Out.WriteLine($"R4A original version={original.Version}; first committed version={afterFirst.Version}; stale HTTP={(int)second.StatusCode}; final permissions={string.Join(',', final.Permissions)}");
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict,
            "the second request starts after the first response but echoes the version both administrators originally read");
        (await IdentityHttpHarness.ReadProblemAsync(second)).GetProperty("code").GetString().ShouldBe("role_concurrency_conflict");
        final.Permissions.ShouldBe(afterFirst.Permissions);
    }

    [Test]
    public async Task R4B_A_sequential_assignment_only_edit_rejects_the_original_membership_version()
    {
        using var organization = await OrganizationScenario.CreateAsync("r4-member");
        var other = await organization.AddMemberAsync("other-admin", Permissions.MembersRead, Permissions.MembersManage,
            Permissions.MembersInvite);
        var membershipId = await organization.AddQuietMemberAsync("target", Permissions.MembersRead);
        var roleA = await CreateRoleAsync(organization, "First replacement", Permissions.MembersRead);
        var roleB = await CreateRoleAsync(organization, "Second replacement", Permissions.MembersInvite);
        var original = await MemberAsync(organization.Owner, organization, membershipId);
        var secondView = await MemberAsync(other.Acting, organization, membershipId);
        secondView.Version.ShouldBe(original.Version);

        using var first = await AssignAsync(organization.Owner, organization, original, roleA.RoleId);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var afterFirst = (await first.Content.ReadFromJsonAsync<MemberRow>())!;
        using var second = await AssignAsync(other.Acting, organization, secondView, roleB.RoleId);
        var final = await MemberAsync(organization.Owner, organization, membershipId);

        TestContext.Out.WriteLine($"R4B original version={original.Version}; first committed version={afterFirst.Version}; stale HTTP={(int)second.StatusCode}; final roles={string.Join(',', final.RoleIds)}; status={final.Status}");
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict,
            "changing only MembershipRoles must invalidate the original membership version even in sequential requests");
        (await IdentityHttpHarness.ReadProblemAsync(second)).GetProperty("code").GetString().ShouldBe("membership_concurrency_conflict");
        final.RoleIds.ShouldBe([roleA.RoleId]);
        final.Status.ShouldBe(nameof(MembershipStatus.Active));
    }

    [Test]
    public async Task R5_A_revoked_invitee_can_return_with_only_the_newly_offered_roles()
    {
        using var organization = await OrganizationScenario.CreateAsync("r5");
        var oldRole = await CreateRoleAsync(organization, "Old authority", Permissions.MembersManage);
        var newRole = await CreateRoleAsync(organization, "New authority", Permissions.MembersRead);
        var email = $"r5-recipient-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, OrganizationScenario.Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var recipient = await Administrator.SignInAsync(harness, $"https://r5-{Guid.NewGuid():N}.localhost", email, organization.TenantId);

        using var initial = await organization.Owner.SendAsync(HttpMethod.Post, Invitations(organization), new { email, roleIds = new[] { oldRole.RoleId } });
        initial.StatusCode.ShouldBe(HttpStatusCode.Created, await initial.Content.ReadAsStringAsync());
        var initialId = (await initial.Content.ReadFromJsonAsync<IssuedRow>())!.InvitationId;
        var initialToken = await InvitationTokenAsync(harness, initialId);
        using var accepted = await recipient.SendAsync(HttpMethod.Post, "/api/invitations/accept", new { token = initialToken });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var membershipId = (await accepted.Content.ReadFromJsonAsync<AcceptedRow>())!.MembershipId;
        (await EffectiveCodesAsync(identityId, organization.TenantId)).ShouldBe([Permissions.MembersManage]);
        var original = await MemberAsync(organization.Owner, organization, membershipId);

        using var revoked = await organization.Owner.SendAsync(HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/members/{membershipId}/revoke", new { version = original.Version });
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await revoked.Content.ReadAsStringAsync());
        var afterRevocation = await MemberAsync(organization.Owner, organization, membershipId);
        afterRevocation.Status.ShouldBe(nameof(MembershipStatus.Revoked));
        afterRevocation.RoleIds.ShouldBeEmpty();
        (await EffectiveCodesAsync(identityId, organization.TenantId)).ShouldBeEmpty();

        using var fresh = await organization.Owner.SendAsync(HttpMethod.Post, Invitations(organization), new { email, roleIds = new[] { newRole.RoleId } });
        TestContext.Out.WriteLine($"R5 issue={(int)initial.StatusCode}; accept={(int)accepted.StatusCode}; revoke={(int)revoked.StatusCode}; new issue={(int)fresh.StatusCode}; body={await fresh.Content.ReadAsStringAsync()}");
        fresh.StatusCode.ShouldBe(HttpStatusCode.Created,
            "C5 allows Revoked -> Active only through a fresh invitation; an existing revoked row cannot block the new offer");
        var freshId = (await fresh.Content.ReadFromJsonAsync<IssuedRow>())!.InvitationId;
        freshId.ShouldNotBe(initialId);
        var freshToken = await InvitationTokenAsync(harness, freshId);
        using var returned = await recipient.SendAsync(HttpMethod.Post, "/api/invitations/accept", new { token = freshToken });
        returned.StatusCode.ShouldBe(HttpStatusCode.OK, await returned.Content.ReadAsStringAsync());
        (await returned.Content.ReadFromJsonAsync<AcceptedRow>())!.MembershipId.ShouldBe(membershipId);
        var final = await MemberAsync(organization.Owner, organization, membershipId);
        final.Status.ShouldBe(nameof(MembershipStatus.Active));
        final.RoleIds.ShouldBe([newRole.RoleId]);
        (await EffectiveCodesAsync(identityId, organization.TenantId)).ShouldBe([Permissions.MembersRead]);
        (await TestApp.ListAsync<TenantMembership>()).Count(member => member.TenantId == organization.TenantId && member.IdentityId == identityId).ShouldBe(1);
    }

    private static string Invitations(OrganizationScenario organization) => $"/api/tenants/{organization.TenantId.Value}/invitations";
    private static string RolePath(OrganizationScenario organization, Guid roleId) => $"/api/tenants/{organization.TenantId.Value}/roles/{roleId}";

    private static async Task<RoleRow> CreateRoleAsync(OrganizationScenario organization, string name, params string[] permissions)
    {
        await organization.Owner.ProveAsync(ProofActions.RoleChange);
        using var response = await organization.Owner.SendAsync(HttpMethod.Post, $"/api/tenants/{organization.TenantId.Value}/roles", new { name, permissions });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RoleRow>())!;
    }

    private static async Task<HttpResponseMessage> UpdateRoleAsync(Administrator actor, OrganizationScenario organization, RoleRow original, string[] permissions)
    {
        await actor.ProveAsync(ProofActions.RoleChange);
        return await actor.SendAsync(HttpMethod.Put, RolePath(organization, original.RoleId), new { name = original.Name, permissions, version = original.Version });
    }

    private static async Task<HttpResponseMessage> AssignAsync(Administrator actor, OrganizationScenario organization, MemberRow original, Guid roleId)
    {
        await actor.ProveAsync(ProofActions.MemberRoleChange);
        return await actor.SendAsync(HttpMethod.Put, $"/api/tenants/{organization.TenantId.Value}/members/{original.MembershipId}/roles",
            new { roleIds = new[] { roleId }, version = original.Version });
    }

    private static async Task<MemberRow> MemberAsync(Administrator actor, OrganizationScenario organization, Guid membershipId) =>
        (await actor.ReadAsync<MemberPage>($"/api/tenants/{organization.TenantId.Value}/members")).Items.Single(member => member.MembershipId == membershipId);

    private static async Task<IReadOnlyList<string>> EffectiveCodesAsync(Guid identityId, TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IEffectivePermissionReader>().GetEffectivePermissionsAsync(identityId, tenantId);
    }

    private static async Task<string> InvitationTokenAsync(IdentityHttpHarness.ProductionHarness harness, Guid invitationId)
    {
        using var scope = harness.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = await context.Invitations.AsNoTracking().SingleAsync(candidate => candidate.Id == InvitationId.From(invitationId));
        var hash = invitation.TokenHash.Value;
        var ciphertext = (await context.OutboxSecrets.AsNoTracking().SingleAsync(secret => secret.VersionedHash == hash)).Ciphertext!;
        return scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("identity-access.registration.outbox-secret.v1").Unprotect(ciphertext);
    }

    private sealed record RoleRow(Guid RoleId, string Name, string[] Permissions, string Version);
    private sealed record MemberRow(Guid MembershipId, string Status, Guid[] RoleIds, string Version);
    private sealed record MemberPage(MemberRow[] Items);
    private sealed record IssuedRow(Guid InvitationId);
    private sealed record AcceptedRow(Guid MembershipId);

    private sealed class InvitationBeforeInsertBarrier(Guid roleId) : SaveChangesInterceptor
    {
        private bool _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int InvitationBackendPid { get; private set; }
        internal int WideningBackendPid { get; private set; }
        internal void Arm() => _armed = true;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_armed || eventData.Context is not { } context) return result;
            if (context.ChangeTracker.Entries<RolePermission>().Any(entry => entry.State == EntityState.Added &&
                entry.Entity.RoleId == RoleId.From(roleId) && entry.Entity.PermissionCode == Permissions.MembersManage))
            {
                context.Database.CurrentTransaction.ShouldNotBeNull();
                WideningBackendPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
            }

            if (context.ChangeTracker.Entries<Invitation>().Any(entry => entry.State == EntityState.Added))
            {
                context.Database.CurrentTransaction.ShouldNotBeNull();
                InvitationBackendPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }
}
