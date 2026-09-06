namespace CleanArchitecture.Application.IdentityAccess.Security;

/// <summary>One named budget: how many attempts a key may spend, and over what fixed window.</summary>
public readonly record struct AttemptBudget(string Scope, int Limit, TimeSpan Window);

public enum AttemptBudgetOutcome
{
    Admitted,
    Exhausted,
    Unavailable
}

/// <summary>
/// What a budget decided, and how long the caller should wait. The two refusals are deliberately distinct: a caller
/// who really spent their attempts is told so, and an outage is told as an outage (IA-REQ-057).
/// </summary>
public readonly record struct AttemptBudgetDecision(AttemptBudgetOutcome Outcome, TimeSpan RetryAfter)
{
    public bool IsAdmitted => Outcome == AttemptBudgetOutcome.Admitted;
}

/// <summary>
/// The port over shared attempt state. One budget holds across every instance and every restart, so a limit cannot
/// be escaped by reaching a different process or by waiting for a deployment.
/// </summary>
public interface ISharedAttemptBudget
{
    /// <summary>
    /// Spends one attempt against <paramref name="key"/>. The key is hashed before it is stored, so an address or an
    /// identity never becomes a row anyone can read back.
    /// </summary>
    Task<AttemptBudgetDecision> SpendAsync(AttemptBudget budget, string key, CancellationToken cancellationToken);
}
