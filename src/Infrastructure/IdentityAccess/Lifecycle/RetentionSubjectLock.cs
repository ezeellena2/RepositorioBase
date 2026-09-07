using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// `SELECT … FOR UPDATE` over the subjects' document rows, which is the coordination C7 names (IA-REQ-056).
/// <para>
/// Ordered by identity, so two lockers reaching for the same pair can never take them in opposite orders and
/// deadlock. Plain `FOR UPDATE` rather than `NOWAIT` or `SKIP LOCKED`: skipping would let a purge quietly proceed
/// past a hold that was being placed — which is the exact outcome the lock exists to prevent — and failing fast
/// would turn the loser into an error instead of the answer the contract gives it.
/// </para>
/// <para>
/// It locks nothing when there is nothing to lock. A subject with no document row has no retention-eligible rows,
/// and `FOR UPDATE` over an empty set is not a lock — which is honest: there is no purge for a hold to race.
/// </para>
/// </summary>
public sealed class RetentionSubjectLock(ApplicationDbContext context) : IRetentionSubjectLock
{
    public async Task LockAsync(IReadOnlyCollection<Guid> subjectIdentityIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectIdentityIds);
        if (subjectIdentityIds.Count == 0) return;

        var ordered = subjectIdentityIds.Distinct().Order().ToArray();
        await context.Database
            .SqlQuery<Guid>($"""
                SELECT "IdentityId" AS "Value"
                FROM "IdentityDocuments"
                WHERE "IdentityId" = ANY({ordered})
                ORDER BY "IdentityId"
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }
}
