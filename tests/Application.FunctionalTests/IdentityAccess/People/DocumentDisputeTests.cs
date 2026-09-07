using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;
using CleanArchitecture.Application.IdentityAccess.People.Documents;
using CleanArchitecture.Application.IdentityAccess.People.Profile;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.People;

/// <summary>
/// Correcting a recorded documentary identity, which takes two parties and never one (IA-REQ-058).
/// <para>
/// The two absences this file exists to hold are what "no support bypass" means: the owner may open a dispute but
/// can never write a document value, and an operator may resolve one only against a stored dispute and never for
/// their own identity. Everything else follows from those.
/// </para>
/// </summary>
public sealed class DocumentDisputeTests : TestBase
{
    private const string Password = PersonalScenario.ValidPassword;
    private const string RecordedNumber = "30111222";
    private const string ClaimedNumber = "30111333";

    [Test]
    public async Task An_owner_opens_a_dispute_over_their_own_document_and_nothing_about_their_access_changes()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();

        var opened = await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"));

        opened.IsSuccess.ShouldBeTrue(opened.Error?.Code);
        opened.Value!.DisputeId.ShouldNotBe(Guid.Empty);

        // The recorded value is untouched until a resolution commits, and the person keeps everything they had.
        var profile = await TestApp.SendAsync(new GetPersonalProfileQuery());
        profile.IsSuccess.ShouldBeTrue();
        profile.Value!.Document!.MaskedNumber.ShouldEndWith(RecordedNumber[^2..]);
        profile.Value.Document.CorrectionAvailable.ShouldBeFalse("a dispute is already open, so another cannot be");
        (await DocumentOf(subject)).PurgedAt.ShouldBeNull();
    }

    /// <summary>
    /// The claimed number is protected on arrival exactly like the recorded one. It is never echoed, logged,
    /// audited, returned or written into an outbox payload — which is what makes a dispute safe to open.
    /// </summary>
    [Test]
    public async Task The_number_somebody_claims_reaches_no_log_no_audit_and_no_response()
    {
        await SubjectWithDocumentAsync();
        await ProveAsync();

        var opened = await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"));
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Code);

        System.Text.Json.JsonSerializer.Serialize(opened.Value).ShouldNotContain(ClaimedNumber);
        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains(ClaimedNumber, StringComparison.Ordinal));
        foreach (var audit in await TestApp.ListAsync<AuditEvent>())
        {
            System.Text.Json.JsonSerializer.Serialize(audit.Metadata).ShouldNotContain(ClaimedNumber);
        }

        foreach (var message in await TestApp.ListAsync<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage>())
        {
            message.Payload.ShouldNotContain(ClaimedNumber);
        }
    }

    [Test]
    public async Task Opening_a_dispute_needs_the_password_proved_a_moment_ago()
    {
        await SubjectWithDocumentAsync();

        var refused = await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("recent_proof_required");
        (await DisputeCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task An_identity_that_records_no_document_has_nothing_to_dispute()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync($"no-document-{Guid.NewGuid():N}@example.test");
        await PersonalScenario.RunWithSessionAsync(identityId);
        await ProveAsync();

        var refused = await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("personal_profile_not_found");
    }

    [Test]
    public async Task One_dispute_per_identity_is_open_at_a_time()
    {
        await SubjectWithDocumentAsync();
        await ProveAsync();
        (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).IsSuccess.ShouldBeTrue();
        await ProveAsync();

        var second = await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", "30111444", "ChangedMyMind"));

        second.IsFailure.ShouldBeTrue();
        second.Error!.Code.ShouldBe("document_dispute_conflict");
        (await DisputeCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task A_corrected_resolution_replaces_the_ciphertext_and_every_fingerprint_together()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        var before = await DocumentOf(subject);
        var operator_ = await OperatorAsync();

        var resolved = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "corrected", "case-2026-0042"));

        resolved.IsSuccess.ShouldBeTrue(resolved.Error?.Code);
        var after = await DocumentOf(subject);
        after.Ciphertext.ShouldNotBe(before.Ciphertext, "a correction replaces the ciphertext");
        after.Fingerprints.ShouldNotBeEmpty("a corrected document is still findable, under its new value");
        after.Fingerprints.Select(print => print.Fingerprint)
            .ShouldNotBe(before.Fingerprints.Select(print => print.Fingerprint), "every retained fingerprint row is replaced");

        var record = (await CorrectionRecordsAsync(subject)).ShouldHaveSingleItem();
        record.DisputeId.ShouldBe(dispute);
        record.EvidenceReference.ShouldBe("case-2026-0042");
        record.ResolvedByMembershipId.ShouldNotBe(Guid.Empty);
        _ = operator_;
    }

    [Test]
    public async Task A_correction_record_holds_no_document_value()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        await OperatorAsync();

        (await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "corrected", "case-2026-0042"))).IsSuccess.ShouldBeTrue();

        var record = (await CorrectionRecordsAsync(subject)).ShouldHaveSingleItem();
        var serialized = System.Text.Json.JsonSerializer.Serialize(record);
        serialized.ShouldNotContain(ClaimedNumber);
        serialized.ShouldNotContain(RecordedNumber);
    }

    /// <summary>
    /// A rejection settles the dispute and leaves the recorded value exactly as it was. The person may then open
    /// another one; a settled dispute is not a permanent bar.
    /// </summary>
    [Test]
    public async Task A_rejected_resolution_changes_the_document_not_at_all()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        var before = await DocumentOf(subject);
        await OperatorAsync();

        (await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "rejected", "case-2026-0042"))).IsSuccess.ShouldBeTrue();

        var after = await DocumentOf(subject);
        after.Ciphertext.ShouldBe(before.Ciphertext);
        (await CorrectionRecordsAsync(subject)).ShouldBeEmpty("nothing was corrected, so there is nothing to record as corrected");
    }

    /// <summary>Without a stored dispute there is no route that writes a document. That absence is the contract.</summary>
    [Test]
    public async Task An_operator_cannot_resolve_a_dispute_that_does_not_exist()
    {
        var subject = await SubjectWithDocumentAsync();
        var before = await DocumentOf(subject);
        await OperatorAsync();

        var refused = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, Guid.NewGuid(), "corrected", "case-2026-0042"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("not_found");
        (await DocumentOf(subject)).Ciphertext.ShouldBe(before.Ciphertext);
    }

    /// <summary>The other absence: an operator is never both parties.</summary>
    [Test]
    public async Task An_operator_cannot_resolve_their_own_dispute()
    {
        var operator_ = await OperatorAsync();
        await GiveDocumentAsync(operator_.IdentityId, RecordedNumber);
        await PersonalScenario.RunWithSessionAsync(operator_.IdentityId);
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        operator_.Resume();

        var refused = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(operator_.IdentityId, dispute, "corrected", "case-2026-0042"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("self_resolution_refused");
        (await CorrectionRecordsAsync(operator_.IdentityId)).ShouldBeEmpty();
    }

    /// <summary>
    /// The claimed number belonging to somebody else is a fact an audited, MFA-proved operator may be told — and
    /// no self-service caller ever is.
    /// </summary>
    [Test]
    public async Task A_claim_on_a_number_somebody_else_records_is_refused_to_the_operator_by_name()
    {
        var subject = await SubjectWithDocumentAsync();
        var other = await SubjectWithDocumentAsync(number: ClaimedNumber);
        await PersonalScenario.RunWithSessionAsync(subject);
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        await OperatorAsync();

        var refused = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "corrected", "case-2026-0042"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("document_already_recorded");
        (await DocumentOf(other)).PurgedAt.ShouldBeNull("the other person's document is not touched by somebody else's dispute");
    }

    /// <summary>An external case reference, not free text and not a place for anything about a person.</summary>
    [TestCase("case 42")]
    [TestCase("case/42")]
    [TestCase("")]
    public async Task An_evidence_reference_outside_the_accepted_shape_is_refused(string reference)
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        await OperatorAsync();

        var refused = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "corrected", reference));

        refused.IsFailure.ShouldBeTrue();
        (await CorrectionRecordsAsync(subject)).ShouldBeEmpty();
    }

    [Test]
    public async Task Resolving_is_not_something_a_session_may_do_because_it_once_proved_a_factor()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        await OperatorAsync();
        TestApp.SetSessionId(Guid.NewGuid()); // A second session, which never stepped up.

        var refused = await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "corrected", "case-2026-0042"));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("recent_mfa_required");
        (await CorrectionRecordsAsync(subject)).ShouldBeEmpty();
    }

    /// <summary>Once a dispute settles, the person may open another. `correctionAvailable` says so.</summary>
    [Test]
    public async Task A_settled_dispute_leaves_the_way_open_for_another()
    {
        var subject = await SubjectWithDocumentAsync();
        await ProveAsync();
        var dispute = (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", ClaimedNumber, "TypedWrongAtSignup"))).Value!.DisputeId;
        await OperatorAsync();
        (await TestApp.SendAsync(new ResolveDocumentDisputeCommand(subject, dispute, "rejected", "case-2026-0042"))).IsSuccess.ShouldBeTrue();

        await PersonalScenario.RunWithSessionAsync(subject);
        var profile = await TestApp.SendAsync(new GetPersonalProfileQuery());
        profile.Value!.Document!.CorrectionAvailable.ShouldBeTrue();

        await ProveAsync();
        (await TestApp.SendAsync(new OpenDocumentDisputeCommand("AR", "DNI", "30111555", "StillWrong"))).IsSuccess.ShouldBeTrue();
    }

    private static async Task ProveAsync() =>
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.DocumentDispute, Password))).IsSuccess.ShouldBeTrue();

    /// <summary>A person with a Personal context and a recorded document, created through the product.</summary>
    private static async Task<Guid> SubjectWithDocumentAsync(string? number = null)
    {
        var email = $"dispute-{Guid.NewGuid():N}@example.test";
        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new RegisterPersonalCommand(email, Password, "Jane Doe", "Jane", number ?? RecordedNumber)))
            .IsSuccess.ShouldBeTrue();

        // Registration is staged: the identity, the Personal tenant and the document all appear at confirmation.
        // The token is read from the envelope this registration wrote rather than from the harness's first mint,
        // because a test that registers twice mints twice.
        (await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail.ConfirmEmailCommand(
            await NewestRegistrationTokenAsync()))).IsSuccess.ShouldBeTrue();

        var identityId = await IdentityIdAsync(email);
        await PersonalScenario.RunWithSessionAsync(identityId);
        return identityId;
    }

    /// <summary>A document for an identity that already exists, seeded as a premise.</summary>
    private static async Task GiveDocumentAsync(Guid identityId, string number)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentProtector>();
        var fingerprints = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentFingerprint>();
        var document = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, number);
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            protector.Protect(document),
            fingerprints.ForRetainedKeys(document),
            DataClassification.Synthetic,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private static async Task<PlatformResolver> OperatorAsync()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var resolver = new PlatformResolver(owner.IdentityId, TestApp.GetSessionId(), owner.PlatformId);
        resolver.Resume();
        return resolver;
    }

    private sealed record PlatformResolver(Guid IdentityId, Guid? SessionId, CleanArchitecture.Domain.IdentityAccess.Tenants.TenantId PlatformId)
    {
        internal void Resume()
        {
            TestApp.SetUserId(IdentityId);
            TestApp.SetSessionId(SessionId);
            TestApp.SetCurrentTenant(PlatformId);
            TestApp.SetApplicationPermissionGranted(true);
        }
    }

    /// <summary>The token the newest staged registration is carrying, read from its sealed envelope.</summary>
    private static async Task<string> NewestRegistrationTokenAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages
            .Where(candidate => candidate.Type == RegisterPersonalCommandHandler.IntentConfirmationMessageType)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstAsync();
        var reader = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader>();
        return (await reader.ReadAsync(message.Id, CancellationToken.None))!;
    }

    private static async Task<Guid> IdentityIdAsync(string email)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var upper = email.ToUpperInvariant();
        return (await context.Users.AsNoTracking().SingleAsync(user => user.NormalizedEmail == upper)).Id;
    }

    private static async Task<IdentityDocument> DocumentOf(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.IdentityDocuments.AsNoTracking()
            .Include(document => document.Fingerprints)
            .SingleAsync(document => document.IdentityId == identityId);
    }

    private static async Task<int> DisputeCountAsync() =>
        (await TestApp.ListAsync<IdentityDocumentDispute>()).Count(dispute => dispute.Status == DocumentDisputeStatus.Open);

    private static async Task<List<IdentityDocumentCorrectionRecord>> CorrectionRecordsAsync(Guid identityId) =>
        (await TestApp.ListAsync<IdentityDocumentCorrectionRecord>()).Where(record => record.SubjectIdentityId == identityId).ToList();
}
