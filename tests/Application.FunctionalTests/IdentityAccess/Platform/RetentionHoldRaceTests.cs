using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// A legal hold racing a purge, over real PostgreSQL, through both real paths (IA-REQ-056, C7).
/// <para>
/// C7 names the coordination and both outcomes: the winner is "whichever takes `SELECT … FOR UPDATE` on the
/// subject's retention-eligible rows first"; a winning hold makes the purge affect zero rows, and a winning purge
/// makes the hold answer `409 retention_hold_subject_purged`. What may never happen is both — a hold an operator
/// was told they placed, over data that was erased anyway.
/// </para>
/// <para>
/// The pause lands on `SaveChangesAsync`, which is the only seam between the executor reading the holds and
/// committing the purge. Every collaborator the cycle has is read strictly before that, so a barrier on any of
/// them would let the hold commit early and pass against unfixed code, proving nothing. There are no sleeps: the
/// competing hold is started while the executor is parked, and the executor is released only afterwards.
/// </para>
/// </summary>
public sealed class RetentionHoldRaceTests : TestBase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A hold that is being placed while the executor is mid-pass must stop it, and the executor must not be
    /// able to decide "nobody holds this" without holding the rows it is about to erase.
    /// <para>
    /// The pause is taken <em>at the lock</em>, which is what makes this deterministic in both directions. With
    /// the lock in place the executor parks there, the competing hold is prepared and committed while it waits,
    /// and the executor then reads the holds and finds it. With the lock removed the pause is never reached at
    /// all and this test fails on its own timeout rather than on a coin toss — a race that has to be lost
    /// reliably is not a race worth writing.
    /// </para>
    /// <para>
    /// No sleeps: every step waits on the other side having reached a named point.
    /// </para>
    /// </summary>
    [Test]
    public async Task An_executor_cannot_decide_nobody_holds_a_subject_without_holding_its_rows()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var subject = await SubjectWithDocumentAsync();
        var barrier = new PurgeBarrier();

        var purging = PurgeAsync(barrier);
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // A hold, taken over the subject's rows and written, and deliberately not yet committed. This is the
        // window C7 describes: confirmed to its own transaction, invisible to anybody who did not take the row.
        //
        // It is raw here rather than through EF because the transaction has to stay open across the barrier
        // release, and this context's retrying execution strategy refuses a transaction it cannot re-run. The two
        // statements are exactly the two the handler issues.
        await using var holding = new Npgsql.NpgsqlConnection(FunctionalTestSetup.ConnectionString);
        await holding.OpenAsync();
        await using var transaction = await holding.BeginTransactionAsync();
        await ExecuteAsync(holding, transaction,
            """SELECT "IdentityId" FROM "IdentityDocuments" WHERE "IdentityId" = @subject ORDER BY "IdentityId" FOR UPDATE""",
            ("subject", subject));
        await ExecuteAsync(holding, transaction,
            """
            INSERT INTO "RetentionLegalHolds"
                ("HoldId", "SubjectIdentityId", "ReasonCode", "Reference", "PlacedAt", "PlacedByMembershipId", "ReleasedAt", "Version")
            VALUES (@holdId, @subject, 'LitigationHold', 'case-race', @placedAt, @placedBy, NULL, 1)
            """,
            ("holdId", Guid.NewGuid()), ("subject", subject), ("placedAt", Now), ("placedBy", Guid.NewGuid()));

        // Released into a lock this transaction is holding. The executor cannot pass until the hold commits.
        barrier.Release.TrySetResult();
        await transaction.CommitAsync();
        var report = await purging.WaitAsync(TimeSpan.FromSeconds(60));

        report.Erased.ShouldBe(0, "a hold that won the row makes the purge affect nothing");
        (await DocumentOf(subject)).PurgedAt.ShouldBeNull("a confirmed hold cannot coexist with a later purge");
        (await ActiveHoldCountAsync(subject)).ShouldBe(1);
    }

    /// <summary>
    /// The other side of the same contention row, through the real route: a purge that committed first makes the
    /// hold impossible, and the operator is told which of the two happened rather than being handed a hold over
    /// nothing. "A hold cannot be made retroactive."
    /// </summary>
    [Test]
    public async Task A_hold_over_a_subject_a_purge_already_reached_is_refused()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var subject = await SubjectWithDocumentAsync();
        (await PurgeAsync(barrier: null)).Erased.ShouldBe(1);

        var refused = await PlaceHoldAsync(owner, subject);

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("retention_hold_subject_purged");
        (await ActiveHoldCountAsync(subject)).ShouldBe(0, "a hold cannot be made retroactive");
    }

    /// <summary>A purge with nothing racing it still erases, so the lock did not simply stop everything.</summary>
    [Test]
    public async Task A_purge_with_nothing_racing_it_still_erases()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var subject = await SubjectWithDocumentAsync();

        var report = await PurgeAsync(barrier: null);

        report.Erased.ShouldBe(1);
        (await DocumentOf(subject)).PurgedAt.ShouldNotBeNull();
    }

    /// <summary>And a hold with nothing racing it is still placed.</summary>
    [Test]
    public async Task A_hold_with_nothing_racing_it_is_still_placed()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var subject = await SubjectWithDocumentAsync();

        var placed = await PlaceHoldAsync(owner, subject);

        placed.IsSuccess.ShouldBeTrue(placed.Error?.Code);
        (await ActiveHoldCountAsync(subject)).ShouldBe(1);
    }

    private static async Task ExecuteAsync(
        Npgsql.NpgsqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new Npgsql.NpgsqlCommand(sql, connection, (Npgsql.NpgsqlTransaction)transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CleanArchitecture.Application.Common.Models.Result<RetentionHoldView>> PlaceHoldAsync(
        PlatformScenario.ActiveOwner owner, Guid subject)
    {
        // The operator's own context, restored explicitly: the step-up is bound to the session that proved it.
        TestApp.SetUserId(owner.IdentityId);
        TestApp.SetCurrentTenant(owner.PlatformId);
        TestApp.SetApplicationPermissionGranted(true);
        return await TestApp.SendAsync(new PlaceRetentionHoldCommand(subject, "LitigationHold", "case-race"));
    }

    /// <summary>
    /// One maintenance pass, optionally parked at the moment between reading the holds and committing the purge.
    /// </summary>
    private static async Task<RetentionMaintenanceReport> PurgeAsync(PurgeBarrier? barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        // Its own context, and therefore its own connection: two transactions on one connection are not a race.
        await using var context = new ApplicationDbContext(options);
        IRetentionSubjectLock subjectLock = new RetentionSubjectLock(context);
        if (barrier is not null) subjectLock = new PausedLock(subjectLock, barrier);

        var cycle = new RetentionMaintenanceCycle(
            context, new FixedPolicy(), new FixedMode(), new FixedTime(Now), subjectLock);
        return await cycle.Bounded(RetentionMaintenanceBounds.Default).RunOnceAsync(CancellationToken.None);
    }

    private static async Task<Guid> SubjectWithDocumentAsync()
    {
        var email = $"race-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, PlatformScenario.ValidPassword);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IIdentityDocumentProtector>();
        var fingerprints = scope.ServiceProvider.GetRequiredService<IIdentityDocumentFingerprint>();
        var document = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, $"4{Random.Shared.Next(1000000, 9999999)}");

        // Recorded long enough ago to be eligible under the policy below, which is what makes this a race at all.
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identityId, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(document), fingerprints.ForRetainedKeys(document),
            DataClassification.Synthetic, Now.AddDays(-400)));
        await context.SaveChangesAsync();
        return identityId;
    }

    private static async Task<IdentityDocument> DocumentOf(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.IdentityDocuments.AsNoTracking().SingleAsync(document => document.IdentityId == identityId);
    }

    private static async Task<int> ActiveHoldCountAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.RetentionLegalHolds.CountAsync(hold => hold.SubjectIdentityId == identityId && hold.ReleasedAt == null);
    }

    private sealed class PurgeBarrier
    {
        private int _armed = 1;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task PauseOnceAsync()
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1) return;
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Stops the executor at the lock. It is the seam and not merely a convenient one: a cycle that never takes
    /// the lock never reaches this, so a test written against it cannot pass on unfixed code by luck.
    /// </summary>
    private sealed class PausedLock(IRetentionSubjectLock inner, PurgeBarrier barrier) : IRetentionSubjectLock
    {
        public async Task LockAsync(IReadOnlyCollection<Guid> subjectIdentityIds, CancellationToken cancellationToken)
        {
            await barrier.PauseOnceAsync();
            await inner.LockAsync(subjectIdentityIds, cancellationToken);
        }
    }

    /// <summary>A policy that erases documents older than a year, supplied rather than configured.</summary>
    private sealed class FixedPolicy : IRetentionPolicy
    {
        public RetentionPolicyDocument? Current { get; } = new(
            "RET-RACE", "1", "platform-operations", new DateOnly(2026, 1, 15), "docs/policies/retention.md", "re-purged",
            [new RetentionCategoryRule(RetentionCategory.PersonalIdentityDocument, TimeSpan.FromDays(365), RetentionTrigger.RecordCreation, RetentionAction.Erase, true)]);
    }

    private sealed class FixedMode : IPersonalDataMode
    {
        public DataClassification Classification => DataClassification.Synthetic;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
