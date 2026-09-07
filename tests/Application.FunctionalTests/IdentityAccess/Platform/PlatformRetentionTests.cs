using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The retention policy an operator can read, and the legal holds they can place and release (IA-REQ-056, C7).
/// <para>
/// The distinction this file exists to hold: a hold stops erasure and nothing else. It is not an account state,
/// it suspends nobody, it refuses no sign-in and it blocks no reactivation — so the tests that place one also
/// check that the person it names can still do exactly what they could before.
/// </para>
/// <para>
/// Everything goes over HTTP because the policy is configuration, and configuration is a property of a running
/// deployment rather than of a request. A test that could not vary it would be testing a constant.
/// </para>
/// </summary>
public sealed class PlatformRetentionTests : TestBase
{
    private const string PolicyId = "RET-2026-01";
    private const string PolicyVersion = "3";
    private const string Source = "docs/policies/retention-2026.md";
    private const string SessionPeriod = "P90D";

    /// <summary>
    /// A policy, as configuration. Nothing about a period, a threshold or a jurisdiction is compiled in, which is
    /// why a test that wants to see one has to supply it.
    /// </summary>
    private static readonly Dictionary<string, string?> ConfiguredPolicy = new()
    {
        ["IdentityAccess:RetentionPolicy:PolicyId"] = PolicyId,
        ["IdentityAccess:RetentionPolicy:Version"] = PolicyVersion,
        ["IdentityAccess:RetentionPolicy:Owner"] = "platform-operations",
        ["IdentityAccess:RetentionPolicy:ApprovedOn"] = "2026-01-15",
        ["IdentityAccess:RetentionPolicy:Source"] = Source,
        ["IdentityAccess:RetentionPolicy:BackupTreatment"] = "restored copies are re-purged before admission",
        ["IdentityAccess:RetentionPolicy:Categories:0:Category"] = nameof(RetentionCategory.SessionRecords),
        ["IdentityAccess:RetentionPolicy:Categories:0:RetentionPeriod"] = SessionPeriod,
        ["IdentityAccess:RetentionPolicy:Categories:0:Trigger"] = nameof(RetentionTrigger.LastActivity),
        ["IdentityAccess:RetentionPolicy:Categories:0:Action"] = nameof(RetentionAction.Erase),
        ["IdentityAccess:RetentionPolicy:Categories:0:EvidenceRequired"] = "true"
    };

    [Test]
    public async Task With_no_policy_configured_there_is_nothing_to_read_and_nothing_to_do()
    {
        await using var operator_ = await OperatorAsync(settings: null);

        using var response = await operator_.GetAsync("/api/platform/retention/policy");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var policy = await ReadAsync(response);
        policy.GetProperty("policyId").ValueKind.ShouldBe(JsonValueKind.Null, "a deployment with no policy says so rather than inventing one");
        policy.GetProperty("categories").GetArrayLength().ShouldBe(0);
        policy.GetProperty("personalDataMode").GetString().ShouldBe("Synthetic");
    }

    [Test]
    public async Task A_configured_policy_is_read_back_exactly_as_it_was_configured()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);

        using var response = await operator_.GetAsync("/api/platform/retention/policy");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var policy = await ReadAsync(response);
        policy.GetProperty("policyId").GetString().ShouldBe(PolicyId);
        policy.GetProperty("version").GetString().ShouldBe(PolicyVersion);
        policy.GetProperty("source").GetString().ShouldBe(Source);
        policy.GetProperty("activeHoldCount").GetInt32().ShouldBe(0);
        var category = policy.GetProperty("categories").EnumerateArray().Single();
        category.GetProperty("category").GetString().ShouldBe(nameof(RetentionCategory.SessionRecords));
        category.GetProperty("retentionPeriod").GetString().ShouldBe(SessionPeriod);
        category.GetProperty("trigger").GetString().ShouldBe(nameof(RetentionTrigger.LastActivity));
        category.GetProperty("action").GetString().ShouldBe(nameof(RetentionAction.Erase));
        category.GetProperty("evidenceRequired").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// A policy that is present but unreadable is no policy. Guessing at half-parsed retention instructions is
    /// the one way this system could delete something nobody asked it to, so it does not guess (IA-REQ-056).
    /// </summary>
    [TestCase("90 days")]
    [TestCase("90")]
    [TestCase("")]
    public async Task A_period_nobody_can_read_leaves_the_deployment_with_no_policy(string period)
    {
        var unreadable = new Dictionary<string, string?>(ConfiguredPolicy)
        {
            ["IdentityAccess:RetentionPolicy:Categories:0:RetentionPeriod"] = period
        };
        await using var operator_ = await OperatorAsync(unreadable);

        var policy = await ReadAsync(await operator_.GetAsync("/api/platform/retention/policy"));

        policy.GetProperty("policyId").ValueKind.ShouldBe(JsonValueKind.Null,
            "a period this system cannot read is not a period it may act on");
        policy.GetProperty("categories").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// A category naming something outside the closed set is dropped, not completed. The rest of the policy is
    /// still what somebody approved, and inventing the missing line would be this system writing policy.
    /// </summary>
    [Test]
    public async Task A_category_outside_the_closed_set_is_dropped_and_the_rest_of_the_policy_stands()
    {
        var withUnknown = new Dictionary<string, string?>(ConfiguredPolicy)
        {
            ["IdentityAccess:RetentionPolicy:Categories:1:Category"] = "SomethingNobodyDefined",
            ["IdentityAccess:RetentionPolicy:Categories:1:RetentionPeriod"] = "P30D",
            ["IdentityAccess:RetentionPolicy:Categories:1:Trigger"] = nameof(RetentionTrigger.RecordCreation),
            ["IdentityAccess:RetentionPolicy:Categories:1:Action"] = nameof(RetentionAction.Erase),
            ["IdentityAccess:RetentionPolicy:Categories:1:EvidenceRequired"] = "true"
        };
        await using var operator_ = await OperatorAsync(withUnknown);

        var policy = await ReadAsync(await operator_.GetAsync("/api/platform/retention/policy"));

        policy.GetProperty("policyId").GetString().ShouldBe(PolicyId);
        policy.GetProperty("categories").EnumerateArray().Single()
            .GetProperty("category").GetString().ShouldBe(nameof(RetentionCategory.SessionRecords));
    }

    [Test]
    public async Task Reading_the_policy_needs_this_session_to_have_proved_the_factor()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy, stepUp: false);

        using var response = await operator_.GetAsync("/api/platform/retention/policy");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("recent_mfa_required");
    }

    [Test]
    public async Task An_operator_places_a_hold_and_the_person_it_names_notices_nothing()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();

        using var placed = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0007" });

        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        placed.Headers.Location.ShouldNotBeNull();
        var hold = await ReadAsync(placed);
        hold.GetProperty("subjectIdentityId").GetGuid().ShouldBe(subject.IdentityId);
        hold.GetProperty("releasedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        hold.GetProperty("placedByMembershipId").GetGuid().ShouldNotBe(Guid.Empty, "a hold records who placed it");

        // The whole effect of a hold, stated as the absence of every other effect.
        (await CanSignInAsync(operator_, subject)).ShouldBeTrue("a hold refuses no sign-in");
        (await StatusAsync(subject.IdentityId)).ShouldBe(IdentityAccountStatus.Active, "a hold is not an account state");
        (await ListAuditAsync()).ShouldContain(item => item.EventType == "personal.data.hold.placed");
    }

    [Test]
    public async Task One_hold_per_subject_and_reason_at_a_time()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        (await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0007" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0008" });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(second)).GetProperty("code").GetString().ShouldBe("retention_hold_conflict");
        (await CountHoldsAsync()).ShouldBe(1);
    }

    /// <summary>
    /// A different reason is a different hold. Releasing one must not release the other, because two reasons are
    /// two decisions and each ends when whoever made it says so.
    /// </summary>
    [Test]
    public async Task A_second_reason_is_a_second_hold_and_they_end_independently()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        var litigation = await PlaceAsync(operator_, subject.IdentityId, "LitigationHold", "case-2026-0007");
        var audit = await PlaceAsync(operator_, subject.IdentityId, "RegulatoryAudit", "audit-2026-11");

        (await operator_.DeleteAsync($"/api/platform/retention/holds/{litigation}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var holds = await ListHoldsAsync();
        holds.Single(hold => hold.HoldId == litigation).ReleasedAt.ShouldNotBeNull();
        holds.Single(hold => hold.HoldId == audit).ReleasedAt.ShouldBeNull();
    }

    /// <summary>
    /// Releasing is idempotent and says nothing about whether the hold existed. An operator repeating a release,
    /// and one naming a hold that never existed, get the same answer.
    /// </summary>
    [Test]
    public async Task Releasing_a_hold_twice_and_releasing_one_that_never_existed_answer_alike()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        var hold = await PlaceAsync(operator_, subject.IdentityId, "LitigationHold", "case-2026-0007");

        (await operator_.DeleteAsync($"/api/platform/retention/holds/{hold}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await operator_.DeleteAsync($"/api/platform/retention/holds/{hold}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await operator_.DeleteAsync($"/api/platform/retention/holds/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CountHoldsAsync()).ShouldBe(1);
    }

    /// <summary>Releasing restores eligibility for erasure and nothing else — the subject was never restricted.</summary>
    [Test]
    public async Task Releasing_a_hold_restores_nothing_because_nothing_was_taken()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        var hold = await PlaceAsync(operator_, subject.IdentityId, "LitigationHold", "case-2026-0007");

        (await operator_.DeleteAsync($"/api/platform/retention/holds/{hold}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CanSignInAsync(operator_, subject)).ShouldBeTrue();
        (await StatusAsync(subject.IdentityId)).ShouldBe(IdentityAccountStatus.Active);
        (await ListAuditAsync()).ShouldContain(item => item.EventType == "personal.data.hold.released");
    }

    /// <summary>
    /// The other side of the contention row C7 names. A purge that committed first makes the hold impossible, and
    /// the operator is told which of the two happened rather than being handed a hold over nothing — "a hold
    /// cannot be made retroactive".
    /// </summary>
    [Test]
    public async Task A_hold_over_a_subject_whose_data_is_already_purged_is_refused()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        await PurgeDocumentOfAsync(subject.IdentityId);

        using var refused = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0007" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString()
            .ShouldBe("retention_hold_subject_purged");
        (await CountHoldsAsync()).ShouldBe(0);
    }

    /// <summary>A subject whose data is still there takes a hold exactly as it always did.</summary>
    [Test]
    public async Task A_hold_over_a_subject_whose_data_is_still_there_is_placed()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        await GiveDocumentAsync(subject.IdentityId);

        using var placed = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0007" });

        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        (await CountHoldsAsync()).ShouldBe(1);
    }

    [Test]
    public async Task A_hold_over_somebody_who_does_not_exist_is_refused()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);

        using var refused = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = Guid.NewGuid(), reasonCode = "LitigationHold", reference = "case-2026-0007" });

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountHoldsAsync()).ShouldBe(0);
    }

    /// <summary>
    /// The reference is an operator's own case number, so it is constrained rather than free text: a retention
    /// record is not a place to write prose about a person.
    /// </summary>
    [TestCase("case number 7")]
    [TestCase("case/2026")]
    [TestCase("")]
    public async Task A_reference_outside_the_accepted_shape_is_refused(string reference)
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();

        using var refused = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountHoldsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Placing_a_hold_is_not_something_a_session_may_do_because_it_once_proved_a_factor()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy, stepUp: false);
        var subject = await SubjectAsync();

        using var refused = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject.IdentityId, reasonCode = "LitigationHold", reference = "case-2026-0007" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("recent_mfa_required");
        (await CountHoldsAsync()).ShouldBe(0);
    }

    /// <summary>The count an operator reads is of holds that are actually holding something.</summary>
    [Test]
    public async Task The_policy_reports_how_many_holds_are_standing()
    {
        await using var operator_ = await OperatorAsync(ConfiguredPolicy);
        var subject = await SubjectAsync();
        var hold = await PlaceAsync(operator_, subject.IdentityId, "LitigationHold", "case-2026-0007");

        (await ReadAsync(await operator_.GetAsync("/api/platform/retention/policy"))).GetProperty("activeHoldCount").GetInt32().ShouldBe(1);
        (await operator_.DeleteAsync($"/api/platform/retention/holds/{hold}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(await operator_.GetAsync("/api/platform/retention/policy"))).GetProperty("activeHoldCount").GetInt32().ShouldBe(0);
    }

    private static async Task<Guid> PlaceAsync(PlatformOperator operator_, Guid subject, string reasonCode, string reference)
    {
        using var placed = await operator_.PostAsync("/api/platform/retention/holds",
            new { subjectIdentityId = subject, reasonCode, reference });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        return (await ReadAsync(placed)).GetProperty("holdId").GetGuid();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A Platform operator over the real pipeline: signed in with a cookie of their own, and having proved the
    /// factor in that session unless the test is about not having.
    /// </summary>
    private static async Task<PlatformOperator> OperatorAsync(IReadOnlyDictionary<string, string?>? settings, bool stepUp = true)
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var harness = IdentityHttpHarness.CreateProductionHarness(settings: settings);
        var operator_ = new PlatformOperator(harness, $"https://retention-{Guid.NewGuid():N}.localhost");
        await operator_.SignInAsync(OwnerEmail, PlatformScenario.ValidPassword);
        if (stepUp)
        {
            (await operator_.PostAsync("/api/platform/mfa/step-up", new { code = PlatformScenario.TotpCode(owner.SharedKey) }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return operator_;
    }

    private const string OwnerEmail = "platform-owner@example.test";

    private static async Task<Subject> SubjectAsync()
    {
        var email = $"retention-subject-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, PlatformScenario.ValidPassword);
        return new Subject(identityId, email);
    }

    /// <summary>Asked through the front door, because "can this person still sign in" is a transport fact.</summary>
    private static async Task<bool> CanSignInAsync(PlatformOperator operator_, Subject subject) =>
        await operator_.CanSignInAsync(subject.Email, PlatformScenario.ValidPassword);

    /// <summary>A recorded document for a subject, seeded as the premise a hold is about.</summary>
    private static async Task GiveDocumentAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentProtector>();
        var fingerprints = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentFingerprint>();
        var document = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, $"4{Random.Shared.Next(1000000, 9999999)}");
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identityId, IdentityDocumentCountry.AR, IdentityDocumentKind.DNI,
            protector.Protect(document), fingerprints.ForRetainedKeys(document),
            DataClassification.Synthetic, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    /// <summary>The state a purge leaves behind: a tombstone, which is what a later hold has nothing to hold.</summary>
    private static async Task PurgeDocumentOfAsync(Guid identityId)
    {
        await GiveDocumentAsync(identityId);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var document = await context.IdentityDocuments
            .Include(candidate => candidate.Fingerprints)
            .SingleAsync(candidate => candidate.IdentityId == identityId);
        document.Purge(DateTimeOffset.UtcNow, "RET-2026-01", "3");
        await context.SaveChangesAsync();
    }

    private static async Task<IdentityAccountStatus> StatusAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Users.AsNoTracking().SingleAsync(user => user.Id == identityId)).Status;
    }

    private static async Task<int> CountHoldsAsync() => (await ListHoldsAsync()).Count;

    private static async Task<List<RetentionLegalHold>> ListHoldsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.RetentionLegalHolds.AsNoTracking().ToListAsync();
    }

    private static async Task<List<AuditEvent>> ListAuditAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.AuditEvents.AsNoTracking().ToListAsync();
    }

    private sealed record Subject(Guid IdentityId, string Email);
}
