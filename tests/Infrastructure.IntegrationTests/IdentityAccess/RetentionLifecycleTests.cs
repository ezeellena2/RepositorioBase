using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// The bounded retention executor against real PostgreSQL (IA-REQ-056, C6's maintenance row).
/// <para>
/// Every test here drives the cycle directly with its own clock and its own bounds. A background loop that had to
/// be waited on would make each of these a race with a timer, and a retention test that is a race is a test that
/// will one day delete something and call it a flake.
/// </para>
/// <para>
/// This is synthetic data throughout. Purging real personal data is the G2 gate and nothing here approaches it.
/// </para>
/// </summary>
public sealed class RetentionLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly List<Guid> _identities = [];

    [TearDown]
    public async Task Remove_only_what_this_test_created()
    {
        if (_identities.Count == 0) return;
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.PersonalDataErasureRecords.Where(record => _identities.Contains(record.SubjectIdentityId)).ExecuteDeleteAsync();
        await context.RetentionLegalHolds.Where(hold => _identities.Contains(hold.SubjectIdentityId)).ExecuteDeleteAsync();
        await context.IdentityDocumentFingerprints.Where(print => _identities.Contains(print.IdentityId)).ExecuteDeleteAsync();
        await context.IdentityDocuments.Where(document => _identities.Contains(document.IdentityId)).ExecuteDeleteAsync();
        await context.UserSessions.Where(session => _identities.Contains(session.IdentityId)).ExecuteDeleteAsync();
        await context.Users.Where(user => _identities.Contains(user.Id)).ExecuteDeleteAsync();
        _identities.Clear();
    }

    [Test]
    public async Task With_no_policy_configured_nothing_is_erased_and_the_cycle_says_why()
    {
        var identity = await IdentityAsync();
        await SettledSessionAsync(identity, lastSeen: Now.AddDays(-200));

        var report = await RunAsync(policy: null);

        report.Erased.ShouldBe(0);
        report.Categories.ShouldContain(category => category.Skipped == RetentionSkipReason.PolicyAbsent);
        (await SessionCountAsync(identity)).ShouldBe(1, "a deployment with no policy performs no destructive action");
        (await SkipReasonsAsync()).ShouldContain("policy_absent");
    }

    /// <summary>
    /// A worker that audited every quarter-hour of finding nothing would bury the one record an operator needs to
    /// see, so an idle pass writes nothing at all.
    /// </summary>
    [Test]
    public async Task An_idle_pass_leaves_no_trace_of_having_run()
    {
        var identity = await IdentityAsync();
        await SettledSessionAsync(identity, lastSeen: Now.AddDays(-1));
        var before = await AuditCountAsync();

        var report = await RunAsync(SessionPolicy());

        report.DidNothing.ShouldBeTrue();
        (await AuditCountAsync()).ShouldBe(before, "an idle pass is not news");
    }

    [Test]
    public async Task Settled_sessions_past_their_period_go_and_a_live_one_stays()
    {
        var identity = await IdentityAsync();
        await SettledSessionAsync(identity, lastSeen: Now.AddDays(-200));
        await SettledSessionAsync(identity, lastSeen: Now.AddDays(-91));
        var live = await LiveSessionAsync(identity, lastSeen: Now.AddDays(-200));

        var report = await RunAsync(SessionPolicy());

        report.Erased.ShouldBe(2);
        var remaining = await SessionsAsync(identity);
        remaining.Count.ShouldBe(1);
        remaining.Single().Id.Value.ShouldBe(live, "a live session is never eligible, however old it is");
    }

    /// <summary>
    /// The bound is the point, not the batch size. What a pass does not reach it simply reaches next time, and
    /// nothing is claimed that is not finished.
    /// </summary>
    [Test]
    public async Task A_run_stops_at_its_bound_and_the_next_one_continues_where_it_stopped()
    {
        var identity = await IdentityAsync();
        for (var index = 0; index < 5; index++) await SettledSessionAsync(identity, lastSeen: Now.AddDays(-200));

        var first = await RunAsync(SessionPolicy(), new RetentionMaintenanceBounds(RowsPerPass: 2, PassesPerRun: 1, Budget: TimeSpan.FromSeconds(60)));

        first.Erased.ShouldBe(2);
        (await SessionCountAsync(identity)).ShouldBe(3);

        var second = await RunAsync(SessionPolicy(), new RetentionMaintenanceBounds(2, 1, TimeSpan.FromSeconds(60)));
        second.Erased.ShouldBe(2);
        (await SessionCountAsync(identity)).ShouldBe(1);
    }

    [Test]
    public async Task Ten_passes_of_two_reach_more_than_one_pass_of_two_does()
    {
        var identity = await IdentityAsync();
        for (var index = 0; index < 5; index++) await SettledSessionAsync(identity, lastSeen: Now.AddDays(-200));

        var report = await RunAsync(SessionPolicy(), new RetentionMaintenanceBounds(RowsPerPass: 2, PassesPerRun: 10, Budget: TimeSpan.FromSeconds(60)));

        report.Erased.ShouldBe(5);
        (await SessionCountAsync(identity)).ShouldBe(0);
    }

    [Test]
    public async Task A_purge_erases_both_halves_and_leaves_a_tombstone_that_names_the_policy()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));

        var report = await RunAsync(DocumentPolicy());

        report.Erased.ShouldBe(1);
        var document = await DocumentOf(identity);
        document.Ciphertext.ShouldBeEmpty();
        document.Fingerprints.ShouldBeEmpty("leaving a fingerprint keeps the number occupying the unique index");
        document.PurgedAt.ShouldNotBeNull();
        document.PurgePolicyId.ShouldBe(PolicyId);
        document.PurgePolicyVersion.ShouldBe(PolicyVersion);
    }

    /// <summary>
    /// A deletion nobody can trace to a policy is a deletion nobody authorized, so the evidence is written in the
    /// same transaction as the erasure it describes.
    /// </summary>
    [Test]
    public async Task A_purge_writes_its_evidence_naming_the_policy_that_authorized_it()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));

        await RunAsync(DocumentPolicy());

        var record = (await ErasureRecordsAsync(identity)).ShouldHaveSingleItem();
        record.Category.ShouldBe(nameof(RetentionCategory.PersonalIdentityDocument));
        record.PolicyId.ShouldBe(PolicyId);
        record.PolicyVersion.ShouldBe(PolicyVersion);
        record.AffectedRowCount.ShouldBeGreaterThan(1, "a document and the fingerprints it carried are both erased");
        record.ExecutedAt.ShouldBe(Now);
    }

    /// <summary>A purged number is reclaimable — which is the whole reason the fingerprints go with the ciphertext.</summary>
    [Test]
    public async Task A_purged_number_can_be_recorded_again_by_somebody_else()
    {
        var first = await IdentityAsync();
        var fingerprint = await DocumentAsync(first, recordedAt: Now.AddDays(-400));
        await RunAsync(DocumentPolicy());

        var second = await IdentityAsync();
        var reclaimed = async () => await DocumentAsync(second, recordedAt: Now, fingerprint: fingerprint);

        await reclaimed.ShouldNotThrowAsync();
    }

    [Test]
    public async Task A_held_subject_is_skipped_and_the_cycle_records_the_hold_as_the_reason()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));
        await HoldAsync(identity);

        var report = await RunAsync(DocumentPolicy());

        report.Erased.ShouldBe(0);
        (await DocumentOf(identity)).PurgedAt.ShouldBeNull("a hold stops erasure, which is the whole of what it does");
        (await ErasureRecordsAsync(identity)).ShouldBeEmpty();
        (await SkipReasonsAsync()).ShouldContain("legal_hold");
    }

    [Test]
    public async Task Releasing_the_hold_lets_the_next_cycle_erase()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));
        await HoldAsync(identity);
        await RunAsync(DocumentPolicy());

        await ReleaseHoldsAsync(identity);
        var report = await RunAsync(DocumentPolicy());

        report.Erased.ShouldBe(1);
        (await DocumentOf(identity)).PurgedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// A row whose classification is not this deployment's is left alone rather than erased. A Real deployment
    /// carrying leftover Synthetic rows runs, and those rows stay ineligible.
    /// </summary>
    [Test]
    public async Task A_row_classified_for_another_deployment_is_ineligible_and_the_cycle_says_so()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));

        var report = await RunAsync(DocumentPolicy(), mode: DataClassification.Real);

        report.Erased.ShouldBe(0);
        (await DocumentOf(identity)).PurgedAt.ShouldBeNull();
        (await SkipReasonsAsync()).ShouldContain("synthetic_classification");
    }

    /// <summary>
    /// Two instances working the same category at once. The second finds it claimed and leaves it alone, rather
    /// than queueing duplicate work behind a run already doing it.
    /// <para>
    /// It is asked of documents rather than sessions on purpose. Deleting a row twice is harmless — the second
    /// delete simply reaches nothing — so a test over sessions would pass whether or not the lock existed. A
    /// purge is not like that: it writes evidence, and two instances purging one document would leave two records
    /// of one erasure, which is a lie about how many there were.
    /// </para>
    /// </summary>
    [Test]
    public async Task Two_instances_working_at_once_leave_one_record_of_one_erasure()
    {
        var identity = await IdentityAsync();
        await DocumentAsync(identity, recordedAt: Now.AddDays(-400));

        var bounds = new RetentionMaintenanceBounds(RowsPerPass: 6, PassesPerRun: 10, Budget: TimeSpan.FromSeconds(60));
        var first = RunAsync(DocumentPolicy(), bounds);
        var second = RunAsync(DocumentPolicy(), bounds);
        var reports = await Task.WhenAll(first, second);

        reports.Sum(report => report.Erased).ShouldBe(1, "one document is erased once, by one of them");
        (await ErasureRecordsAsync(identity)).Count.ShouldBe(1, "two records of one erasure is a lie about how many there were");
        (await DocumentOf(identity)).PurgedAt.ShouldNotBeNull();
    }

    private const string PolicyId = "RET-TEST";
    private const string PolicyVersion = "1";

    private static RetentionPolicyDocument SessionPolicy() => Policy(
        new RetentionCategoryRule(RetentionCategory.SessionRecords, TimeSpan.FromDays(90), RetentionTrigger.LastActivity, RetentionAction.Erase, true));

    private static RetentionPolicyDocument DocumentPolicy() => Policy(
        new RetentionCategoryRule(RetentionCategory.PersonalIdentityDocument, TimeSpan.FromDays(365), RetentionTrigger.RecordCreation, RetentionAction.Erase, true));

    private static RetentionPolicyDocument Policy(params RetentionCategoryRule[] categories) => new(
        PolicyId, PolicyVersion, "platform-operations", new DateOnly(2026, 1, 15), "docs/policies/retention.md", "re-purged", categories);

    private async Task<RetentionMaintenanceReport> RunAsync(
        RetentionPolicyDocument? policy,
        RetentionMaintenanceBounds? bounds = null,
        DataClassification mode = DataClassification.Synthetic)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cycle = new RetentionMaintenanceCycle(
            context,
            new FixedPolicy(policy),
            new FixedMode(mode),
            new FixedTime(Now),
            new RetentionSubjectLock(context), TestMetrics.Instance);
        return await cycle.Bounded(bounds ?? RetentionMaintenanceBounds.Default).RunOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// The tenant the worker acts for. It is a premise rather than the thing under test: a deployment with no
    /// Platform has nowhere to record what a purge did, and this executor does nothing in one.
    /// </summary>
    private static async Task PlatformExistsAsync()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await context.Tenants.AnyAsync(tenant => tenant.Type == TenantType.Platform)) return;

        var platform = Tenant.CreatePlatform();
        platform.Activate();
        context.Tenants.Add(platform);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> IdentityAsync()
    {
        await PlatformExistsAsync();

        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = $"retention-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Status = CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus.Active
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        _identities.Add(user.Id);
        return user.Id;
    }

    private static async Task<Guid> SettledSessionAsync(Guid identityId, DateTimeOffset lastSeen)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = UserSession.Create(identityId, lastSeen, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        session.Revoke(lastSeen);
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id.Value;
    }

    private static async Task<Guid> LiveSessionAsync(Guid identityId, DateTimeOffset lastSeen)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Created long ago but still live, so the only thing that can keep it is being live.
        var session = UserSession.Create(identityId, lastSeen, TimeSpan.FromDays(3650), TimeSpan.FromDays(3650));
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id.Value;
    }

    private static async Task<string> DocumentAsync(Guid identityId, DateTimeOffset recordedAt, string? fingerprint = null)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var value = fingerprint ?? $"k1:v1:{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))}";
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            $"cipher-{Guid.NewGuid():N}",
            [new DocumentFingerprintValue(1, value)],
            DataClassification.Synthetic,
            recordedAt));
        await context.SaveChangesAsync();
        return value;
    }

    private static async Task HoldAsync(Guid identityId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.RetentionLegalHolds.Add(RetentionLegalHold.Place(identityId, "LitigationHold", "case-1", Guid.NewGuid(), Now));
        await context.SaveChangesAsync();
    }

    private static async Task ReleaseHoldsAsync(Guid identityId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.RetentionLegalHolds.Where(hold => hold.SubjectIdentityId == identityId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ReleasedAt, Now));
    }

    private static async Task<IdentityDocument> DocumentOf(Guid identityId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.IdentityDocuments.AsNoTracking()
            .Include(document => document.Fingerprints)
            .SingleAsync(document => document.IdentityId == identityId);
    }

    private static async Task<List<PersonalDataErasureRecord>> ErasureRecordsAsync(Guid identityId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.PersonalDataErasureRecords.AsNoTracking()
            .Where(record => record.SubjectIdentityId == identityId).ToListAsync();
    }

    private static async Task<List<UserSession>> SessionsAsync(Guid identityId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.UserSessions.AsNoTracking().Where(session => session.IdentityId == identityId).ToListAsync();
    }

    private static async Task<int> SessionCountAsync(Guid identityId) => (await SessionsAsync(identityId)).Count;

    private static async Task<int> AuditCountAsync()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.AuditEvents.CountAsync(item => item.EventType.StartsWith("personal.data."));
    }

    private static async Task<List<string>> SkipReasonsAsync()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await context.AuditEvents.AsNoTracking()
            .Where(item => item.EventType == "personal.data.retention.skipped").ToListAsync();
        return events.Select(item => item.Metadata["reason"]).ToList();
    }

    private sealed class FixedPolicy(RetentionPolicyDocument? current) : IRetentionPolicy
    {
        public RetentionPolicyDocument? Current { get; } = current;
    }

    private sealed class FixedMode(DataClassification classification) : IPersonalDataMode
    {
        public DataClassification Classification { get; } = classification;
    }

    /// <summary>The clock this cycle reads. Retention is about elapsed time, so it is supplied rather than waited for.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
