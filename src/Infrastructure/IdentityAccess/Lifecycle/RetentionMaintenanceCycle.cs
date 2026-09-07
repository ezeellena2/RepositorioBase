using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// What one maintenance run is allowed to do. The numbers are **product defaults** accepted with C6: they bound
/// the executor, not the policy — a retention <em>period</em> is configuration and appears nowhere in source.
/// </summary>
public sealed record RetentionMaintenanceBounds(int RowsPerPass, int PassesPerRun, TimeSpan Budget)
{
    public static RetentionMaintenanceBounds Default { get; } = new(500, 10, TimeSpan.FromSeconds(60));
}

/// <summary>Why a category did nothing, in the vocabulary C7 accepted.</summary>
public enum RetentionSkipReason
{
    PolicyAbsent,
    LegalHold,
    SyntheticClassification,
    TriggerNotImplemented
}

public sealed record RetentionCategoryOutcome(RetentionCategory Category, int Erased, RetentionSkipReason? Skipped);

public sealed record RetentionMaintenanceReport(IReadOnlyList<RetentionCategoryOutcome> Categories)
{
    public int Erased => Categories.Sum(category => category.Erased);

    public bool DidNothing => Categories.All(category => category is { Erased: 0, Skipped: null });
}

/// <summary>
/// The bounded executor behind retention (IA-REQ-056). It has no public route, no permission and no caller that
/// is a person: erasure is driven by policy, and an operator's only powers over it are to read what the policy
/// says and to stop it with a hold.
/// <para>
/// Every run is bounded three ways at once — rows per pass, passes per run, and a wall-clock budget — because
/// each of them fails differently: a large batch holds locks too long, an unbounded loop never yields, and a
/// cheap-looking pass over a big table can still outlast the interval. Whichever runs out first ends the run, and
/// what was not reached this time is simply reached next time; nothing is claimed that is not finished.
/// </para>
/// <para>
/// Single-flight per category, through an advisory lock held for the transaction. A second instance finding the
/// lock taken leaves that category alone rather than waiting — waiting would just queue up duplicate work behind
/// a run that is already doing it.
/// </para>
/// </summary>
public sealed class RetentionMaintenanceCycle(
    ApplicationDbContext context,
    IRetentionPolicy policy,
    IPersonalDataMode personalDataMode,
    TimeProvider timeProvider,
    IRetentionSubjectLock subjectLock)
{
    /// <summary>The fifth advisory space, after registration, sessions, external subjects and role authority.</summary>
    private const int LockSpace = 0x5E5513;

    private RetentionMaintenanceBounds _bounds = RetentionMaintenanceBounds.Default;
    private TenantId? _platform;

    /// <summary>Narrower bounds, so a test can drive the mechanism without seeding five hundred rows.</summary>
    public RetentionMaintenanceCycle Bounded(RetentionMaintenanceBounds bounds)
    {
        _bounds = bounds;
        return this;
    }

    public async Task<RetentionMaintenanceReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        // Audit records are tenant-scoped and this worker acts for Platform, so a deployment that has not been
        // bootstrapped has nowhere to record what it did. Erasing without being able to say so is not something
        // this does, and doing nothing is the safe way to be unable to (IA-REQ-056, IA-REQ-026).
        _platform = await PlatformTenantAsync(cancellationToken);
        if (_platform is null) return new RetentionMaintenanceReport([]);

        var configured = policy.Current;

        // No policy is not an error and not a reason to guess. It is a state this deployment runs in, and in it
        // nothing destructive happens at all — in either personal-data mode.
        if (configured is null)
        {
            await RecordSkipAsync(null, RetentionSkipReason.PolicyAbsent, cancellationToken);
            return new RetentionMaintenanceReport([new RetentionCategoryOutcome(default, 0, RetentionSkipReason.PolicyAbsent)]);
        }

        var deadline = timeProvider.GetUtcNow().Add(_bounds.Budget);
        var outcomes = new List<RetentionCategoryOutcome>();

        foreach (var rule in configured.Categories.Where(rule => rule.Action == RetentionAction.Erase))
        {
            if (timeProvider.GetUtcNow() >= deadline) break;
            outcomes.Add(await RunCategoryAsync(configured, rule, deadline, cancellationToken));
        }

        return new RetentionMaintenanceReport(outcomes);
    }

    private async Task<RetentionCategoryOutcome> RunCategoryAsync(
        RetentionPolicyDocument configured,
        RetentionCategoryRule rule,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var erased = 0;
        RetentionSkipReason? skipped = null;

        for (var pass = 0; pass < _bounds.PassesPerRun; pass++)
        {
            if (timeProvider.GetUtcNow() >= deadline) break;

            var pending = await RunPassAsync(configured, rule, cancellationToken);
            if (pending.Skipped is { } reason)
            {
                skipped = reason;
                break;
            }

            erased += pending.Erased;

            // A pass that did not fill its batch has reached the end of what is eligible. Running again would
            // only re-read the same rows.
            if (pending.Erased < _bounds.RowsPerPass) break;
        }

        if (skipped is { } recorded) await RecordSkipAsync(rule.Category, recorded, cancellationToken);
        return new RetentionCategoryOutcome(rule.Category, erased, skipped);
    }

    /// <summary>
    /// One pass, in its own transaction — through the context's execution strategy, because the advisory lock is
    /// scoped to that transaction and a retry has to retake it rather than continue inside a transaction that is
    /// already gone.
    /// </summary>
    private Task<(int Erased, RetentionSkipReason? Skipped)> RunPassAsync(
        RetentionPolicyDocument configured,
        RetentionCategoryRule rule,
        CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // Single-flight. A category another instance is already working is left alone rather than waited for.
            var claimed = await context.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockSpace}, {(int)rule.Category}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!claimed) return (0, (RetentionSkipReason?)null);

            var now = timeProvider.GetUtcNow();
            var due = now - rule.RetentionPeriod;

            var outcome = rule.Category switch
            {
                RetentionCategory.SessionRecords => await EraseSessionsAsync(rule, due, cancellationToken),
                RetentionCategory.PersonalIdentityDocument => await EraseDocumentsAsync(configured, rule, due, now, cancellationToken),

                // Everything else the closed set names is a category this executor does not erase yet. Skipping
                // it and saying so is the honest answer; quietly treating it as "nothing to do" is not.
                _ => (0, (RetentionSkipReason?)RetentionSkipReason.TriggerNotImplemented)
            };

            await transaction.CommitAsync(cancellationToken);
            return outcome;
        });

    /// <summary>
    /// Revoked and expired sessions past their period. A live session is never eligible however old it is, which
    /// is why the window is applied on top of a state rather than instead of one.
    /// </summary>
    private async Task<(int Erased, RetentionSkipReason? Skipped)> EraseSessionsAsync(
        RetentionCategoryRule rule, DateTimeOffset due, CancellationToken cancellationToken)
    {
        if (rule.Trigger == RetentionTrigger.AccountClosure) return (0, RetentionSkipReason.TriggerNotImplemented);

        var settled = context.UserSessions.Where(session =>
            session.RevokedAt != null || session.AbsoluteExpiresAt <= DateTimeOffset.UtcNow || session.IdleExpiresAt <= DateTimeOffset.UtcNow);

        var eligible = rule.Trigger == RetentionTrigger.LastActivity
            ? settled.Where(session => session.LastSeenAt <= due)
            : settled.Where(session => session.CreatedAt <= due);

        var batch = await eligible.OrderBy(session => session.Id).Take(_bounds.RowsPerPass)
            .Select(session => session.Id).ToListAsync(cancellationToken);
        if (batch.Count == 0) return (0, null);

        var erased = await context.UserSessions.Where(session => batch.Contains(session.Id)).ExecuteDeleteAsync(cancellationToken);
        return (erased, null);
    }

    /// <summary>
    /// Documents past their period, one subject at a time, each with its evidence. The ciphertext and every
    /// retained fingerprint go together — leaving the fingerprints would keep the number occupying the unique
    /// index after its ciphertext was gone, which is the opposite of reclaimable.
    /// </summary>
    private async Task<(int Erased, RetentionSkipReason? Skipped)> EraseDocumentsAsync(
        RetentionPolicyDocument configured,
        RetentionCategoryRule rule,
        DateTimeOffset due,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (rule.Trigger != RetentionTrigger.RecordCreation) return (0, RetentionSkipReason.TriggerNotImplemented);

        var mode = personalDataMode.Classification;
        var candidates = await context.IdentityDocuments
            .Include(document => document.Fingerprints)
            .Where(document => document.PurgedAt == null && document.RecordedAt <= due && document.Classification == mode)
            .OrderBy(document => document.IdentityId)
            .Take(_bounds.RowsPerPass)
            .ToListAsync(cancellationToken);

        // A row whose classification is not this deployment's is left alone rather than erased. A Real
        // deployment carrying leftover Synthetic rows starts, and those rows stay ineligible (IA-REQ-056).
        if (candidates.Count == 0)
        {
            var ineligible = await context.IdentityDocuments
                .AnyAsync(document => document.PurgedAt == null && document.RecordedAt <= due && document.Classification != mode, cancellationToken);
            return (0, ineligible ? RetentionSkipReason.SyntheticClassification : null);
        }

        // Taken before the holds are read, and that order is the fix. At READ COMMITTED this statement blocks
        // until any hold transaction already touching these rows commits or rolls back; the read that follows is
        // a new statement snapshot and therefore sees the hold that just committed. Reading first and locking
        // afterwards would leave exactly the window C7 describes — a hold confirmed between the read and the
        // purge — and a purge that committed over a confirmed hold is the one outcome this may not produce.
        var candidateIds = candidates.Select(document => document.IdentityId).ToArray();
        await subjectLock.LockAsync(candidateIds, cancellationToken);

        var held = await context.RetentionLegalHolds
            .Where(hold => hold.ReleasedAt == null && candidateIds.Contains(hold.SubjectIdentityId))
            .Select(hold => hold.SubjectIdentityId)
            .ToListAsync(cancellationToken);

        var erased = 0;
        var skippedForHold = false;
        foreach (var document in candidates)
        {

            if (held.Contains(document.IdentityId))
            {
                skippedForHold = true;
                continue;
            }

            var rows = 1 + document.Fingerprints.Count;
            document.Purge(now, configured.PolicyId, configured.Version);

            // Written here, in the same SaveChanges as the erasure it describes. Evidence that could commit
            // without the deletion, or a deletion that could commit without its evidence, would be neither.
            context.Set<PersonalDataErasureRecord>().Add(PersonalDataErasureRecord.Of(
                document.IdentityId, rule.Category.ToString(), configured.PolicyId, configured.Version, rows, now));
            RecordPurge(document.IdentityId, rule.Category);
            erased++;
        }

        if (erased > 0) await context.SaveChangesAsync(cancellationToken);
        return (erased, erased == 0 && skippedForHold ? RetentionSkipReason.LegalHold : null);
    }

    private void RecordPurge(Guid subjectIdentityId, RetentionCategory category)
    {
        context.AuditEvents.Add(AuditEvent.Create(
            _platform!.Value,
            null,
            "personal.data.purged",
            $"retention-purge-{subjectIdentityId:N}",
            new Dictionary<string, string>
            {
                ["code"] = "personal.data.purged",
                ["outcome"] = "purged",
                ["reason"] = category.ToString()
            }));
    }

    /// <summary>
    /// Once per cycle, and never for an idle pass. A worker that audited every quarter-hour of finding nothing
    /// would bury the one record an operator needs to see (IA-REQ-056).
    /// </summary>
    private async Task RecordSkipAsync(RetentionCategory? category, RetentionSkipReason reason, CancellationToken cancellationToken)
    {
        context.AuditEvents.Add(AuditEvent.Create(
            _platform!.Value,
            null,
            "personal.data.retention.skipped",
            $"retention-skip-{category?.ToString() ?? "all"}-{Guid.NewGuid():N}",
            new Dictionary<string, string>
            {
                ["code"] = "personal.data.retention.skipped",
                ["outcome"] = "skipped",
                ["reason"] = Name(reason)
            }));
        await context.Database.CreateExecutionStrategy().ExecuteAsync(() => context.SaveChangesAsync(cancellationToken));
    }

    private static string Name(RetentionSkipReason reason) => reason switch
    {
        RetentionSkipReason.PolicyAbsent => "policy_absent",
        RetentionSkipReason.LegalHold => "legal_hold",
        RetentionSkipReason.SyntheticClassification => "synthetic_classification",
        _ => "trigger_not_implemented"
    };

    /// <summary>The tenant this worker acts for, resolved once per run.</summary>
    private async Task<TenantId?> PlatformTenantAsync(CancellationToken cancellationToken)
    {
        var platform = await context.Tenants
            .Where(tenant => tenant.Type == TenantType.Platform)
            .Select(tenant => (TenantId?)tenant.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return platform;
    }
}
