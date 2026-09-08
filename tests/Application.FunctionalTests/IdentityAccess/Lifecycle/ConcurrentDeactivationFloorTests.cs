using System.Net;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Lifecycle;

/// <summary>
/// The administrator floor when two administrators leave at the same moment (IA-REQ-053/054).
/// <para>
/// The sequential case is covered by the review's own regression, which this file deliberately does not touch:
/// one administrator parks their account, and the next is told there is nobody left. Sequentially the answer can
/// be read from committed rows. Overlapping, it cannot — each transaction reads a world in which the other has
/// not committed yet, so each sees a replacement and each believes it is the one who may go.
/// </para>
/// <para>
/// The lock the deactivation already takes is keyed on the identity leaving, which is the right key for the
/// sessions it revokes and the wrong one for the organization it might empty: two identities take two different
/// keys and never meet. What the floor needs is coordination per tenant, and this is the case that says so.
/// </para>
/// </summary>
public sealed class ConcurrentDeactivationFloorTests : TestBase
{
    [Test]
    public async Task Two_administrators_deactivating_at_once_leave_one_of_them_standing()
    {
        using var scenario = await OrganizationScenario.CreateAsync("floor-race");
        var deputy = await scenario.AddMemberAsync("deputy", Permissions.MembersManage, Permissions.RolesManage);

        // Both are administrators before anything happens, so whichever loses does so on the floor and not on a
        // premise that was never true.
        (await AdministratorsAsync(scenario.TenantId)).ShouldBe(2);

        var pause = new FloorReadPause(scenario.OwnerId);
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            services.AddScoped<IRoleAdministrationStore>(provider =>
                new PausedRoleStore(new RoleAdministrationStore(provider.GetRequiredService<ApplicationDbContext>()), pause));
        });
        var host = $"https://floor-race-{Guid.NewGuid():N}.localhost";
        var ownerEmail = (await IdentityHttpHarness.GetUserAsync(scenario.OwnerId)).Email!;
        var deputyEmail = (await IdentityHttpHarness.GetUserAsync(deputy.IdentityId)).Email!;
        var leavingOwner = await Administrator.SignInAsync(harness, host, ownerEmail, scenario.TenantId);
        var leavingDeputy = await Administrator.SignInAsync(harness, host, deputyEmail, scenario.TenantId);
        await leavingOwner.ProveAsync(ProofActions.AccountDeactivate);
        await leavingDeputy.ProveAsync(ProofActions.AccountDeactivate);
        pause.Arm();

        // The owner is held after it has read the floor and before it has committed anything, which is the only
        // instant at which the deputy can read a world where the owner is still an administrator.
        var owner = leavingOwner.SendAsync(HttpMethod.Post, "/api/identity/account/deactivate", new { });
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Issued while that read is still uncommitted: this is the overlap, and it is made by construction
        // rather than by a delay somebody tuned.
        var second = leavingDeputy.SendAsync(HttpMethod.Post, "/api/identity/account/deactivate", new { });
        pause.Release.TrySetResult();

        using var ownerResult = await owner.WaitAsync(TimeSpan.FromSeconds(30));
        using var deputyResult = await second.WaitAsync(TimeSpan.FromSeconds(30));
        var ownerState = await StatusAsync(scenario.OwnerId);
        var deputyState = await StatusAsync(deputy.IdentityId);
        var remaining = await AdministratorsAsync(scenario.TenantId);
        TestContext.Out.WriteLine(
            $"owner={(int)ownerResult.StatusCode}; deputy={(int)deputyResult.StatusCode}; " +
            $"states={ownerState}/{deputyState}; administrators left={remaining}");

        // Which of the two wins is the database's to decide, so the assertion is on what must be true either
        // way: one of them left, the other was told it could not, and the organization still has somebody.
        var answers = new[] { ownerResult.StatusCode, deputyResult.StatusCode };
        answers.Count(status => status == HttpStatusCode.NoContent).ShouldBe(1, "exactly one of two overlapping departures may take effect");
        var refused = answers.Single(status => status != HttpStatusCode.NoContent);
        refused.ShouldBe(HttpStatusCode.Conflict, "the one that would empty the organization is refused, not failed");
        var problem = await IdentityHttpHarness.ReadProblemAsync(
            ownerResult.StatusCode == HttpStatusCode.Conflict ? ownerResult : deputyResult);
        problem.GetProperty("code").GetString().ShouldBe("last_administrator_required");

        remaining.ShouldBe(1, "an organization is never left with nobody who can administer it");
        new[] { ownerState, deputyState }.Count(state => state == IdentityAccountStatus.Active)
            .ShouldBe(1, "the refused departure leaves that account exactly as it was — not reactivated, and not parked");
    }

    /// <summary>
    /// Counted the way the floor counts, from a scope of its own, so the assertion reads committed rows rather
    /// than anything either request left tracked.
    /// </summary>
    private static async Task<int> AdministratorsAsync(TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRoleAdministrationStore>()
            .CountAdministratorsAsync(tenantId, CancellationToken.None);
    }

    private static async Task<IdentityAccountStatus> StatusAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == identityId).Select(user => user.Status).SingleAsync();
    }

    /// <summary>
    /// Holds one departure after it has read the floor and before it has written anything. Armed for exactly one
    /// identity, so the other request runs at full speed and the overlap is the one the case is about.
    /// </summary>
    private sealed class FloorReadPause(Guid leavingIdentityId)
    {
        private bool _armed;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm() => _armed = true;

        public async Task AfterFloorReadAsync(Guid excludedIdentityId, CancellationToken cancellationToken)
        {
            if (!_armed || excludedIdentityId != leavingIdentityId) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    /// <summary>The real store, with one seam: the floor read the leaving identity makes about itself.</summary>
    private sealed class PausedRoleStore(IRoleAdministrationStore inner, FloorReadPause pause) : IRoleAdministrationStore
    {
        public Task<RolePage> ListAsync(TenantId tenantId, int limit, string? cursor, CancellationToken ct) => inner.ListAsync(tenantId, limit, cursor, ct);
        public Task<RoleView?> FindAsync(TenantId tenantId, Guid roleId, CancellationToken ct) => inner.FindAsync(tenantId, roleId, ct);
        public Task<IReadOnlyList<string>> GrantableCodesAsync(TenantId tenantId, Guid actorId, CancellationToken ct) => inner.GrantableCodesAsync(tenantId, actorId, ct);
        public Task<IReadOnlyList<string>?> HeldCodesAsync(TenantId tenantId, Guid roleId, CancellationToken ct) => inner.HeldCodesAsync(tenantId, roleId, ct);
        public Task<int> CountAdministratorsAsync(TenantId tenantId, CancellationToken ct) => inner.CountAdministratorsAsync(tenantId, ct);

        public async Task<int> CountAdministratorsExceptAsync(TenantId tenantId, Guid identityId, CancellationToken ct)
        {
            var count = await inner.CountAdministratorsExceptAsync(tenantId, identityId, ct);
            await pause.AfterFloorReadAsync(identityId, ct);
            return count;
        }

        public Task<RoleWriteResult> CreateAsync(TenantId tenantId, RoleEdit edit, CancellationToken ct) => inner.CreateAsync(tenantId, edit, ct);
        public Task<RoleWriteResult> UpdateAsync(TenantId tenantId, Guid roleId, RoleEdit edit, string version, CancellationToken ct) => inner.UpdateAsync(tenantId, roleId, edit, version, ct);
        public Task<RoleWriteResult> RetireAsync(TenantId tenantId, Guid roleId, string version, CancellationToken ct) => inner.RetireAsync(tenantId, roleId, version, ct);
    }
}
