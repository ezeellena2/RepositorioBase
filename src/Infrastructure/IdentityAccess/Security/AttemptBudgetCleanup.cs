using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Security;

/// <summary>
/// Removes attempt-budget rows whose window has closed (IA-REQ-057).
/// <para>
/// The table is written by anyone who can reach a bounded route, which is the point of it and also the reason it
/// needs sweeping: a caller who varies the key — a fresh address per request, an account spelling nobody owns —
/// writes a row per key, and nothing in the spending path can tell that from ordinary traffic. What bounds the
/// abuse is not refusing to write the row, which would hand the attacker the limit they wanted removed, but
/// removing it once its window has closed and it decides nothing.
/// </para>
/// </summary>
public sealed class AttemptBudgetCleanup(ApplicationDbContext context, TimeProvider timeProvider)
{
    /// <summary>
    /// Rows removed per run. A **product default**: large enough to drain a busy quarter-hour, small enough that
    /// a table nobody swept for a month is drained over several runs rather than in one lock nothing else survives.
    /// </summary>
    public const int MaxRows = 5_000;

    private int _maxRows = MaxRows;

    /// <summary>A smaller bound, so a test can watch a sweep stop short without writing five thousand rows.</summary>
    public AttemptBudgetCleanup Bounded(int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        _maxRows = maxRows;
        return this;
    }

    /// <summary>
    /// Deletes the oldest closed windows first, up to the bound. Oldest first so a table that fell behind drains
    /// in the order it filled, and bounded so one neglected sweep cannot hold a lock over the whole of it.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await context.IdentityAttemptBudgets
            .Where(row => row.ExpiresAt <= now)
            .OrderBy(row => row.ExpiresAt)
            .Take(_maxRows)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
