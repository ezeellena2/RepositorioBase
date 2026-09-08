using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What keeps the attempt-budget table from being a place anyone can grow without bound (IA-REQ-057).
/// <para>
/// A caller who varies the key writes a row per key, and the spending path cannot tell that from real traffic —
/// refusing to write would simply remove the limit. So the answer is a sweep, and what a sweep must never do is
/// remove a window that is still deciding something.
/// </para>
/// </summary>
public sealed class AttemptBudgetCleanupTests
{
    private static readonly DateTimeOffset Now = new(2031, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _scopes = [];

    [TearDown]
    public async Task Remove_only_this_tests_budgets()
    {
        if (_scopes.Count == 0) return;
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.IdentityAttemptBudgets.Where(budget => _scopes.Contains(budget.Scope)).ExecuteDeleteAsync();
        _scopes.Clear();
    }

    [Test]
    public async Task A_closed_window_is_swept_and_an_open_one_is_left_deciding()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var closed = Budget(TimeSpan.FromMinutes(15));
        var open = Budget(TimeSpan.FromHours(24));

        // Spent an hour ago: the fifteen-minute window it belongs to closed long before the sweep runs.
        await Adapter(scope, Now.AddHours(-1)).SpendAsync(closed, "yesterdays-attacker", CancellationToken.None);
        await Adapter(scope, Now).SpendAsync(open, "somebody-still-limited", CancellationToken.None);

        var swept = await new AttemptBudgetCleanup(context, new FixedTime(Now)).RunOnceAsync(CancellationToken.None);

        swept.ShouldBe(1);
        (await context.IdentityAttemptBudgets.CountAsync(row => row.Scope == closed.Scope)).ShouldBe(0);
        (await context.IdentityAttemptBudgets.CountAsync(row => row.Scope == open.Scope))
            .ShouldBe(1, "a window that has not closed is still refusing attempts, and sweeping it would refund them");
    }

    [Test]
    public async Task A_sweep_takes_no_more_than_its_bound_and_the_next_one_takes_the_rest()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var budget = Budget(TimeSpan.FromMinutes(15));
        var adapter = Adapter(scope, Now.AddHours(-1));
        for (var key = 0; key < 4; key++) await adapter.SpendAsync(budget, $"key-{key}", CancellationToken.None);

        var bounded = new AttemptBudgetCleanup(context, new FixedTime(Now)).Bounded(3);

        (await bounded.RunOnceAsync(CancellationToken.None)).ShouldBe(3, "a sweep is bounded, so one long-neglected table cannot hold a lock for all of it");
        (await bounded.RunOnceAsync(CancellationToken.None)).ShouldBe(1, "and what it did not reach is reached next time");
        (await context.IdentityAttemptBudgets.CountAsync(row => row.Scope == budget.Scope)).ShouldBe(0);
    }

    private AttemptBudget Budget(TimeSpan window)
    {
        var scope = $"test.cleanup.{Guid.NewGuid():N}";
        _scopes.Add(scope);
        return new AttemptBudget(scope, 5, window);
    }

    private static ISharedAttemptBudget Adapter(IServiceScope scope, DateTimeOffset now) =>
        new PostgreSqlAttemptBudget(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), new FixedTime(now), TestMetrics.Instance);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
