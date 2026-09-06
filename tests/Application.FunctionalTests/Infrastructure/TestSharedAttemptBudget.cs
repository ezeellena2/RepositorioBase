using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// The real shared budget, with one thing a test can do that configuration cannot: make the store unreachable.
/// <para>
/// Failing closed at the edge of that store is a promise the contract makes to a caller — `503`, not `429` — and a
/// promise nobody exercises is a promise nobody keeps. Every other call goes to the real adapter and the real table.
/// </para>
/// </summary>
public sealed class TestSharedAttemptBudget(ISharedAttemptBudget inner) : ISharedAttemptBudget
{
    public Task<AttemptBudgetDecision> SpendAsync(AttemptBudget budget, string key, CancellationToken cancellationToken) =>
        TestApp.IsAttemptBudgetUnavailable()
            ? Task.FromResult(new AttemptBudgetDecision(AttemptBudgetOutcome.Unavailable, TimeSpan.FromSeconds(30)))
            : inner.SpendAsync(budget, key, cancellationToken);
}
