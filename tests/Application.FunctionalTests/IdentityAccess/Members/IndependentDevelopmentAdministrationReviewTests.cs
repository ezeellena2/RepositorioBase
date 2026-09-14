using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Members;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>Independent review reproductions. Assertions describe accepted behavior and may remain red.</summary>
[Category("IndependentDevelopmentReview")]
public sealed class IndependentDevelopmentAdministrationReviewTests : TestBase
{
    [Test]
    public async Task Two_administrators_cannot_sequentially_deactivate_and_leave_no_active_identity()
    {
        using var organization = await OrganizationScenario.CreateAsync("review-floor");
        var deputy = await organization.AddMemberAsync("deputy", Permissions.MembersManage, Permissions.RolesManage);
        (await organization.Owner.GetAsync("/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await deputy.Acting.GetAsync("/api/identity/context")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await organization.Owner.ProveAsync(ProofActions.AccountDeactivate);
        using var first = await organization.Owner.SendAsync(HttpMethod.Post, "/api/identity/account/deactivate", new { });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent, await first.Content.ReadAsStringAsync());
        (await StatusAsync(organization.OwnerId)).ShouldBe(IdentityAccountStatus.SelfDeactivated);
        (await StatusAsync(deputy.IdentityId)).ShouldBe(IdentityAccountStatus.Active);

        await deputy.Acting.ProveAsync(ProofActions.AccountDeactivate);
        using var second = await deputy.Acting.SendAsync(HttpMethod.Post, "/api/identity/account/deactivate", new { });
        var ownerState = await StatusAsync(organization.OwnerId);
        var deputyState = await StatusAsync(deputy.IdentityId);
        var ownerContext = await organization.Owner.GetAsync("/api/identity/context");
        var deputyContext = await deputy.Acting.GetAsync("/api/identity/context");
        TestContext.Out.WriteLine($"First deactivate={(int)first.StatusCode}; second={(int)second.StatusCode}; owner={ownerState}; deputy={deputyState}; contexts={(int)ownerContext.StatusCode}/{(int)deputyContext.StatusCode}");

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict,
            "the remaining effective administrator cannot count a self-deactivated identity as a replacement");
        (await IdentityHttpHarness.ReadProblemAsync(second)).GetProperty("code").GetString().ShouldBe("last_administrator_required");
        deputyState.ShouldBe(IdentityAccountStatus.Active);
    }

    [Test]
    public async Task Ownership_cannot_be_transferred_to_a_self_deactivated_identity_with_an_active_membership()
    {
        using var organization = await OrganizationScenario.CreateAsync("review-recipient");
        var recipient = await organization.AddMemberAsync("recipient", Permissions.MembersRead);
        await recipient.Acting.ProveAsync(ProofActions.AccountDeactivate);
        using var parked = await recipient.Acting.SendAsync(HttpMethod.Post, "/api/identity/account/deactivate", new { });
        parked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await parked.Content.ReadAsStringAsync());
        (await StatusAsync(recipient.IdentityId)).ShouldBe(IdentityAccountStatus.SelfDeactivated);
        var target = await MemberAsync(organization.Owner, organization.TenantId, recipient.MembershipId);
        target.Status.ShouldBe(nameof(MembershipStatus.Active), "C6 keeps the membership when its identity parks the account");

        await organization.Owner.ProveAsync(ProofActions.OwnershipTransfer);
        using var transferred = await organization.Owner.SendAsync(HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/ownership/transfer", new { toMembershipId = target.MembershipId, version = target.Version });
        var final = await MemberAsync(organization.Owner, organization.TenantId, target.MembershipId);
        TestContext.Out.WriteLine($"Recipient deactivate={(int)parked.StatusCode}; transfer={(int)transferred.StatusCode}; identity={await StatusAsync(recipient.IdentityId)}; membership={final.Status}; recipientIsOwner={final.IsOwner}");

        transferred.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "IA-REQ-053 requires a confirmed active recipient identity, not only an active membership row");
        (await IdentityHttpHarness.ReadProblemAsync(transferred)).GetProperty("code").GetString().ShouldBe("invalid_membership_operation");
        final.IsOwner.ShouldBeFalse();
    }

    [Test]
    public async Task Suspending_a_member_requires_the_recent_primary_proof_retained_by_accepted_C6()
    {
        using var organization = await OrganizationScenario.CreateAsync("review-status-proof");
        var membershipId = await organization.AddQuietMemberAsync("target", Permissions.MembersRead);
        var target = await MemberAsync(organization.Owner, organization.TenantId, membershipId);
        target.IsOwner.ShouldBeFalse();
        target.Status.ShouldBe(nameof(MembershipStatus.Active));
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.RecentIdentityProofs.AnyAsync()).ShouldBeFalse("no proof was obtained in this scenario");
        }

        using var suspended = await organization.Owner.SendAsync(HttpMethod.Post,
            $"/api/tenants/{organization.TenantId.Value}/members/{membershipId}/suspend", new { version = target.Version });
        var final = await MemberAsync(organization.Owner, organization.TenantId, membershipId);
        TestContext.Out.WriteLine($"Suspend without proof={(int)suspended.StatusCode}; original={target.Status}; final={final.Status}");

        suspended.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "SPEC 14.5 retains C6's proof requirement and SPEC 14.6 requires it for suspend/reactivate");
        (await IdentityHttpHarness.ReadProblemAsync(suspended)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        final.Status.ShouldBe(nameof(MembershipStatus.Active));
    }

    [Test]
    public async Task An_assignment_cannot_confer_a_permission_revoked_after_its_ceiling_read_before_commit()
    {
        using var organization = await OrganizationScenario.CreateAsync("review-ceiling");
        var actor = await organization.AddMemberAsync("limited", Permissions.MembersManage, Permissions.RolesManage);
        var targetId = await organization.AddQuietMemberAsync("target", Permissions.MembersRead);
        await organization.Owner.ProveAsync(ProofActions.RoleChange);
        using var created = await organization.Owner.SendAsync(HttpMethod.Post, $"/api/tenants/{organization.TenantId.Value}/roles",
            new { name = "Offered administration", permissions = new[] { Permissions.RolesManage } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var offered = (await created.Content.ReadFromJsonAsync<RoleView>())!;
        var actorRole = (await organization.Owner.ReadAsync<PaginatedList<RoleView>>($"/api/tenants/{organization.TenantId.Value}/roles"))
            .Items.Single(role => role.Name == "limited-role");
        var target = await MemberAsync(organization.Owner, organization.TenantId, targetId);
        var pause = new AssignmentPause(targetId, actorRole.RoleId);
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            services.AddSingleton<ISaveChangesInterceptor>(pause);
            services.AddScoped<IMembershipAdministrationStore>(provider =>
            {
                var context = provider.GetRequiredService<ApplicationDbContext>();
                return new PausedMembershipStore(new MembershipAdministrationStore(context), context, pause);
            });
        });
        var host = $"https://review-ceiling-race-{Guid.NewGuid():N}.localhost";
        var actorEmail = (await IdentityHttpHarness.GetUserAsync(actor.IdentityId)).Email!;
        var ownerEmail = (await IdentityHttpHarness.GetUserAsync(organization.OwnerId)).Email!;
        var acting = await Administrator.SignInAsync(harness, host, actorEmail, organization.TenantId);
        var owner = await Administrator.SignInAsync(harness, host, ownerEmail, organization.TenantId);
        await acting.ProveAsync(ProofActions.MemberRoleChange);
        await owner.ProveAsync(ProofActions.RoleChange);
        pause.Arm();

        var assignment = acting.SendAsync(HttpMethod.Put,
            $"/api/tenants/{organization.TenantId.Value}/members/{targetId}/roles",
            new { roleIds = new[] { offered.RoleId }, version = target.Version });
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var narrowed = await owner.SendAsync(HttpMethod.Put,
                $"/api/tenants/{organization.TenantId.Value}/roles/{actorRole.RoleId}",
                new { name = actorRole.Name, permissions = new[] { Permissions.MembersManage }, version = actorRole.Version });
            narrowed.StatusCode.ShouldBe(HttpStatusCode.OK, await narrowed.Content.ReadAsStringAsync());
            using var current = await acting.GetAsync("/api/identity/context");
            current.StatusCode.ShouldBe(HttpStatusCode.OK);
            var codes = (await IdentityHttpHarness.ReadJsonAsync(current)).GetProperty("permissions").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            codes.ShouldContain(Permissions.MembersManage);
            codes.ShouldNotContain(Permissions.RolesManage, "the revocation must already be visible to the affected identity at the server");
            pause.AssignmentBackendPid.ShouldBeGreaterThan(0);
            pause.RevocationBackendPid.ShouldBeGreaterThan(0);
            pause.AssignmentBackendPid.ShouldNotBe(pause.RevocationBackendPid, "the changes use independent PostgreSQL connections");
        }
        finally
        {
            pause.Release.TrySetResult();
        }

        using var result = await assignment.WaitAsync(TimeSpan.FromSeconds(20));
        using var readScope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var effective = readScope.ServiceProvider.GetRequiredService<IEffectivePermissionReader>();
        var finalCodes = await effective.GetEffectivePermissionsAsync(target.IdentityId, organization.TenantId);
        TestContext.Out.WriteLine($"Assignment PID={pause.AssignmentBackendPid}; revocation PID={pause.RevocationBackendPid}; assignment={(int)result.StatusCode}; final target permissions={string.Join(',', finalCodes)}");

        finalCodes.ShouldNotContain(Permissions.RolesManage,
            "the grant ceiling must still hold at commit after another administrator removes the actor's authority");
        result.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.Forbidden);
    }

    private static async Task<IdentityAccountStatus> StatusAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == identityId).Select(user => user.Status).SingleAsync();
    }

    private static async Task<MemberView> MemberAsync(Administrator actor, TenantId tenantId, Guid membershipId) =>
        (await actor.ReadAsync<PaginatedList<MemberView>>($"/api/tenants/{tenantId.Value}/members")).Items.Single(member => member.MembershipId == membershipId);

    private sealed class AssignmentPause(Guid targetMembershipId, Guid narrowedRoleId) : SaveChangesInterceptor
    {
        private bool _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AssignmentBackendPid { get; private set; }
        public int RevocationBackendPid { get; private set; }
        public void Arm() => _armed = true;

        public async Task BeforeReplaceAsync(ApplicationDbContext context, Guid membershipId, CancellationToken cancellationToken)
        {
            if (!_armed || membershipId != targetMembershipId) return;
            context.Database.CurrentTransaction.ShouldNotBeNull();
            AssignmentBackendPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_armed && eventData.Context is { } context && context.ChangeTracker.Entries<Role>()
                    .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == RoleId.From(narrowedRoleId)))
            {
                context.Database.CurrentTransaction.ShouldNotBeNull();
                RevocationBackendPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PausedMembershipStore(IMembershipAdministrationStore inner, ApplicationDbContext context, AssignmentPause pause)
        : IMembershipAdministrationStore
    {
        public Task<PaginatedList<MemberView>> ListAsync(TenantId tenantId, PaginationQuery pagination, CancellationToken ct) => inner.ListAsync(tenantId, pagination, ct);
        public Task<PaginatedList<InvitationSummaryView>> ListInvitationsAsync(TenantId tenantId, PaginationQuery pagination, CancellationToken ct) => inner.ListInvitationsAsync(tenantId, pagination, ct);
        public Task<MemberView?> FindAsync(TenantId tenantId, Guid membershipId, CancellationToken ct) => inner.FindAsync(tenantId, membershipId, ct);
        public Task<IReadOnlyList<string>?> CodesOfRolesAsync(TenantId tenantId, IReadOnlyList<Guid> roleIds, CancellationToken ct) => inner.CodesOfRolesAsync(tenantId, roleIds, ct);
        public async Task<MembershipWriteResult> ReplaceRolesAsync(TenantId tenantId, Guid membershipId, IReadOnlyList<Guid> roleIds, string version, CancellationToken ct)
        {
            await pause.BeforeReplaceAsync(context, membershipId, ct);
            return await inner.ReplaceRolesAsync(tenantId, membershipId, roleIds, version, ct);
        }
        public Task<MembershipWriteResult> SetStatusAsync(TenantId tenantId, Guid membershipId, MembershipStatus target, string version, CancellationToken ct) => inner.SetStatusAsync(tenantId, membershipId, target, version, ct);
        public Task<MembershipWriteResult> TransferOwnershipAsync(TenantId tenantId, Guid membershipId, string version, CancellationToken ct) => inner.TransferOwnershipAsync(tenantId, membershipId, version, ct);
        public Task<Guid?> OwnerAsync(TenantId tenantId, CancellationToken ct) => inner.OwnerAsync(tenantId, ct);
    }
}
