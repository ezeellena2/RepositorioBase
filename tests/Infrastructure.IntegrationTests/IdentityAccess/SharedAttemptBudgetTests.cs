using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What the shared attempt budget holds true against a real PostgreSQL row (IA-REQ-057).
/// <para>
/// These are persistence checks. Two adapter instances here share one process, so nothing in this file is evidence
/// that a budget holds across service instances or restarts — that is Task 27's, and it is not claimed here.
/// </para>
/// </summary>
public sealed class SharedAttemptBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _scopes = [];

    private AttemptBudget Budget(int limit, TimeSpan? window = null)
    {
        var scope = $"test.budget.{Guid.NewGuid():N}";
        _scopes.Add(scope);
        return new AttemptBudget(scope, limit, window ?? TimeSpan.FromMinutes(15));
    }

    private static ISharedAttemptBudget Adapter(IServiceScope scope, DateTimeOffset now) =>
        new PostgreSqlAttemptBudget(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new FixedTime(now), TestMetrics.Instance);

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
    public async Task Exactly_the_budget_is_admitted_and_the_next_attempt_is_refused()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(3);
        var adapter = Adapter(scope, Now);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            (await adapter.SpendAsync(budget, "someone", CancellationToken.None)).IsAdmitted
                .ShouldBeTrue($"attempt {attempt} of 3 is inside the budget");
        }

        var refused = await adapter.SpendAsync(budget, "someone", CancellationToken.None);
        refused.Outcome.ShouldBe(AttemptBudgetOutcome.Exhausted);
        refused.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async Task One_key_spending_its_budget_does_not_refuse_another()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(1);
        var adapter = Adapter(scope, Now);

        (await adapter.SpendAsync(budget, "first", CancellationToken.None)).IsAdmitted.ShouldBeTrue();
        (await adapter.SpendAsync(budget, "first", CancellationToken.None)).Outcome.ShouldBe(AttemptBudgetOutcome.Exhausted);
        (await adapter.SpendAsync(budget, "second", CancellationToken.None)).IsAdmitted.ShouldBeTrue();
    }

    [Test]
    public async Task A_spent_window_stays_spent_for_an_adapter_that_never_saw_it_spent()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(1);

        (await Adapter(scope, Now).SpendAsync(budget, "someone", CancellationToken.None)).IsAdmitted.ShouldBeTrue();

        using var second = TestServices.CreateScope();
        (await Adapter(second, Now).SpendAsync(budget, "someone", CancellationToken.None))
            .Outcome.ShouldBe(AttemptBudgetOutcome.Exhausted, "the budget is the row, not the object that read it");
    }

    [Test]
    public async Task The_next_window_starts_the_budget_again()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(1, TimeSpan.FromMinutes(15));

        (await Adapter(scope, Now).SpendAsync(budget, "someone", CancellationToken.None)).IsAdmitted.ShouldBeTrue();
        (await Adapter(scope, Now.AddMinutes(5)).SpendAsync(budget, "someone", CancellationToken.None))
            .Outcome.ShouldBe(AttemptBudgetOutcome.Exhausted, "the same window is still the same window");
        (await Adapter(scope, Now.AddMinutes(20)).SpendAsync(budget, "someone", CancellationToken.None))
            .IsAdmitted.ShouldBeTrue();
    }

    [Test]
    public async Task Parallel_attempts_at_the_threshold_admit_the_budget_and_no_more()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(5);

        var scopes = Enumerable.Range(0, 12).Select(_ => TestServices.CreateScope()).ToList();
        try
        {
            var decisions = await Task.WhenAll(scopes.Select(instance =>
                Adapter(instance, Now).SpendAsync(budget, "crowd", CancellationToken.None)));

            decisions.Count(decision => decision.IsAdmitted).ShouldBe(5);
            decisions.Count(decision => decision.Outcome == AttemptBudgetOutcome.Exhausted).ShouldBe(7);
        }
        finally
        {
            foreach (var instance in scopes) instance.Dispose();
        }
    }

    [Test]
    public async Task An_unreachable_store_refuses_the_attempt_and_says_it_is_an_outage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=absent;Username=absent;Password=absent;Timeout=1;Command Timeout=1")
            .Options;
        await using var unreachable = new ApplicationDbContext(options);
        var adapter = new PostgreSqlAttemptBudget(unreachable, new FixedTime(Now), TestMetrics.Instance);

        var decision = await adapter.SpendAsync(Budget(3), "someone", CancellationToken.None);

        decision.Outcome.ShouldBe(AttemptBudgetOutcome.Unavailable, "failing closed is the refusal; calling it 'too many attempts' would be a lie");
        decision.IsAdmitted.ShouldBeFalse();
        decision.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task The_stored_row_holds_a_digest_rather_than_the_key_it_bounded()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var budget = Budget(3);

        await Adapter(scope, Now).SpendAsync(budget, "jane.doe@example.test", CancellationToken.None);

        var row = await context.IdentityAttemptBudgets.SingleAsync(entry => entry.Scope == budget.Scope);
        row.KeyHash.ShouldNotContain("jane.doe");
        row.KeyHash.ShouldNotContain("@");
        row.Count.ShouldBe(1);
        row.WindowStart.ShouldBeLessThanOrEqualTo(Now);
        row.ExpiresAt.ShouldBeGreaterThan(Now);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
