using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.Platform.Retention;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>Independent retention regressions using synthetic rows in the existing disposable PostgreSQL harness.</summary>
[Category("IndependentDevelopmentReview")]
public sealed class IndependentDevelopmentRetentionReviewTests
{
    private const string PolicyId = "RET-INDEPENDENT-DEV";
    private const string PolicyVersion = "1";
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly List<Guid> _identities = [];

    [TearDown]
    public async Task Remove_only_rows_created_by_this_test()
    {
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
    public async Task A_held_revoked_session_is_preserved_until_hold_release()
    {
        var identity = await IdentityAsync();
        await RevokedSessionAsync(identity);
        await HoldAsync(identity);

        var heldRun = await RunAsync(SessionPolicy());

        (await SessionCountAsync(identity)).ShouldBe(1, "a committed legal hold must preserve the subject's old revoked session");
        heldRun.Erased.ShouldBe(0);
        heldRun.Categories.ShouldContain(category => category.Category == RetentionCategory.SessionRecords && category.Skipped == RetentionSkipReason.LegalHold);
        (await ErasureRecordsAsync(identity)).ShouldBeEmpty();

        await ReleaseHoldsAsync(identity);
        var releasedRun = await RunAsync(SessionPolicy());

        (await SessionCountAsync(identity)).ShouldBe(0, "releasing the hold restores eligibility for this configured erasure");
        releasedRun.Erased.ShouldBe(1);
    }

    [Test]
    public async Task Erasing_a_revoked_session_writes_required_policy_evidence()
    {
        var identity = await IdentityAsync();
        await RevokedSessionAsync(identity);

        var report = await RunAsync(SessionPolicy());

        (await SessionCountAsync(identity)).ShouldBe(0, "the session must actually be erased before its required evidence is checked");
        report.Erased.ShouldBe(1);
        var record = (await ErasureRecordsAsync(identity)).ShouldHaveSingleItem("a completed session erasure requires its own policy evidence");
        record.Category.ShouldBe(nameof(RetentionCategory.SessionRecords));
        record.PolicyId.ShouldBe(PolicyId);
        record.PolicyVersion.ShouldBe(PolicyVersion);
        record.AffectedRowCount.ShouldBe(1);
        record.ExecutedAt.ShouldBe(Now);

        var audit = (await PurgeAuditAsync(identity)).ShouldHaveSingleItem();
        audit.Metadata["reason"].ShouldBe(nameof(RetentionCategory.SessionRecords));
        audit.Metadata["outcome"].ShouldBe("purged");
    }

    [Test]
    public async Task A_held_first_document_does_not_starve_the_next_eligible_subject()
    {
        var first = await IdentityAsync();
        var second = await IdentityAsync();
        await DocumentAsync(first);
        await DocumentAsync(second);
        var ordered = await DocumentIdentityOrderAsync([first, second]);
        var heldIdentity = ordered[0];
        var eligibleIdentity = ordered[1];
        await HoldAsync(heldIdentity);

        var report = await RunAsync(DocumentPolicy(), new RetentionMaintenanceBounds(1, 2, TimeSpan.FromSeconds(60)));

        var held = await DocumentOfAsync(heldIdentity);
        held.PurgedAt.ShouldBeNull();
        held.Ciphertext.ShouldNotBeEmpty();
        held.Fingerprints.ShouldHaveSingleItem();
        var eligible = await DocumentOfAsync(eligibleIdentity);
        eligible.PurgedAt.ShouldNotBeNull("a held first row must not prevent a later eligible subject from being reached within two passes");
        eligible.Ciphertext.ShouldBeEmpty();
        eligible.Fingerprints.ShouldBeEmpty();
        report.Erased.ShouldBe(1);
        (await ErasureRecordsAsync(heldIdentity)).ShouldBeEmpty();
        (await ErasureRecordsAsync(eligibleIdentity)).ShouldHaveSingleItem();
    }

    private static RetentionPolicyDocument SessionPolicy() => Policy(new RetentionCategoryRule(
        RetentionCategory.SessionRecords, TimeSpan.FromDays(90), RetentionTrigger.LastActivity, RetentionAction.Erase, true));

    private static RetentionPolicyDocument DocumentPolicy() => Policy(new RetentionCategoryRule(
        RetentionCategory.PersonalIdentityDocument, TimeSpan.FromDays(365), RetentionTrigger.RecordCreation, RetentionAction.Erase, true));

    private static RetentionPolicyDocument Policy(RetentionCategoryRule category) => new(
        PolicyId, PolicyVersion, "platform-operations", new DateOnly(2026, 1, 15), "docs/policies/retention.md", "re-purged", [category]);

    private static async Task<RetentionMaintenanceReport> RunAsync(RetentionPolicyDocument policy, RetentionMaintenanceBounds? bounds = null)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cycle = new RetentionMaintenanceCycle(
            context, new FixedPolicy(policy), new SyntheticMode(), new FixedTime(), new RetentionSubjectLock(context), TestMetrics.Instance);
        return await cycle.Bounded(bounds ?? new RetentionMaintenanceBounds(2, 2, TimeSpan.FromSeconds(60))).RunOnceAsync(CancellationToken.None);
    }

    private async Task<Guid> IdentityAsync()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await context.Tenants.AnyAsync(tenant => tenant.Type == TenantType.Platform))
        {
            var platform = Tenant.CreatePlatform();
            platform.Activate();
            context.Tenants.Add(platform);
        }

        var email = $"retention-review-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Status = IdentityAccountStatus.Active
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        _identities.Add(user.Id);
        return user.Id;
    }

    private static async Task RevokedSessionAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lastSeen = Now.AddDays(-200);
        var session = UserSession.Create(identity, lastSeen, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        session.Revoke(lastSeen);
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
    }

    private static async Task DocumentAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fingerprint = $"k1:v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}";
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identity, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, $"synthetic-cipher-{Guid.NewGuid():N}",
            [new DocumentFingerprintValue(1, fingerprint)], DataClassification.Synthetic, Now.AddDays(-400)));
        await context.SaveChangesAsync();
    }

    private static async Task HoldAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.RetentionLegalHolds.Add(RetentionLegalHold.Place(identity, "LitigationHold", "independent-review", Guid.NewGuid(), Now));
        await context.SaveChangesAsync();
    }

    private static async Task ReleaseHoldsAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.RetentionLegalHolds.Where(hold => hold.SubjectIdentityId == identity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ReleasedAt, Now));
    }

    private static async Task<int> SessionCountAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.UserSessions.CountAsync(session => session.IdentityId == identity);
    }

    private static async Task<List<Guid>> DocumentIdentityOrderAsync(Guid[] identities)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.IdentityDocuments.Where(document => identities.Contains(document.IdentityId))
            .OrderBy(document => document.IdentityId).Select(document => document.IdentityId).ToListAsync();
    }

    private static async Task<IdentityDocument> DocumentOfAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.IdentityDocuments.AsNoTracking().Include(document => document.Fingerprints)
            .SingleAsync(document => document.IdentityId == identity);
    }

    private static async Task<List<PersonalDataErasureRecord>> ErasureRecordsAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.PersonalDataErasureRecords.AsNoTracking().Where(record => record.SubjectIdentityId == identity).ToListAsync();
    }

    private static async Task<List<AuditEvent>> PurgeAuditAsync(Guid identity)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var correlation = $"retention-purge-{identity:N}";
        return await context.AuditEvents.AsNoTracking()
            .Where(audit => audit.EventType == "personal.data.purged" && audit.CorrelationId == correlation).ToListAsync();
    }

    private sealed class FixedPolicy(RetentionPolicyDocument policy) : IRetentionPolicy
    {
        public RetentionPolicyDocument? Current { get; } = policy;
    }

    private sealed class SyntheticMode : IPersonalDataMode
    {
        public DataClassification Classification => DataClassification.Synthetic;
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
