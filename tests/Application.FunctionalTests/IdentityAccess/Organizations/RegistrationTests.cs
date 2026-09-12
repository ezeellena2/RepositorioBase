using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

public sealed class RegistrationTests : TestBase
{
    /// <summary>
    /// Two submissions that normalize to the same intent are one registration, so they may leave only one thing
    /// finalizable behind: the canonical key is what stops a resent form from becoming a second organization.
    /// The organization it becomes exists only once the mailed proof is spent, so that is where the graph the
    /// two submissions produced between them can be counted.
    /// </summary>
    [Test]
    public async Task Normalized_equivalent_anonymous_requests_are_neutral_and_finalize_into_one_registration_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var command = new RegisterOrganizationCommand($"owner-{suffix}@example.test", "Testing1234!", "Northwind Registration", "30-12345678-1");
        var normalizedVariant = command with
        {
            Email = $"  OWNER-{suffix.ToUpperInvariant()}@EXAMPLE.TEST ",
            LegalName = "  northwind registration  ",
            Cuit = "30 12345678 1"
        };

        var first = await TestApp.SendAsync(command);
        var second = await TestApp.SendAsync(normalizedVariant);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await AssertSingleRegistrationIntentAsync();

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();
        await AssertSingleOwnerOrganizationGraphAsync();
    }

    [Test]
    public async Task Concurrent_equivalent_anonymous_requests_return_the_same_neutral_result_without_a_partial_duplicate_graph()
    {
        var command = NewCommand();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(command, barrier)));

        results.ShouldAllBe(result => result.IsSuccess);
        await AssertSingleRegistrationIntentAsync();

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();
        await AssertSingleOwnerOrganizationGraphAsync();
    }

    /// <summary>
    /// Both initiations for one address are accepted, because refusing the second would answer whether the first
    /// exists. The address is still single-owner where that is decided: the first proof creates the identity, the
    /// second finds it there and settles as a conflict, so neither a second identity nor a second graph appears.
    /// </summary>
    [Test]
    public async Task Concurrent_anonymous_requests_with_different_intents_and_the_same_email_complete_neutrally_without_a_duplicate_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"race-email-{suffix}@example.test";
        var first = new RegisterOrganizationCommand(email, "Testing1234!", "Northwind One", "30-12345678-1");
        var second = new RegisterOrganizationCommand(email.ToUpperInvariant(), "Testing1234!", "Northwind Two", "30-87654321-0");
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(first, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(second, barrier)));

        results.ShouldAllBe(result => result.IsSuccess);
        var submissions = await TestApp.ListAsync<RegistrationSubmission>();
        submissions.Count.ShouldBe(2);
        submissions.Count(submission => submission.CompletedAt.HasValue && submission.Outcome == RegistrationSubmissionOutcome.Accepted).ShouldBe(2);
        (await TestApp.CountAsync<PendingRegistrationIntent>()).ShouldBe(2);
        await AssertNoExclusiveClaimAsync();

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.RawTokenAt(0)))).IsSuccess.ShouldBeTrue();
        var loser = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.RawTokenAt(1)));

        loser.IsFailure.ShouldBeTrue();
        loser.Error!.Code.ShouldBe("registration_conflict");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "the second proof of one address must not create a second identity for it");
        await AssertSettledIntentsAsync(created: 1, conflicted: 1);
        await AssertSingleOrganizationGraphAsync();
    }

    /// <summary>
    /// Neither initiation claims the CUIT, so both are accepted and which of them will lose is visible to nobody.
    /// The uniqueness they raced for is enforced where the proof is spent, and the loser still keeps the account
    /// that proof earned: losing a CUIT is not a reason to refuse someone the address they demonstrably own.
    /// </summary>
    [Test]
    public async Task Concurrent_anonymous_requests_with_different_intents_and_the_same_cuit_complete_their_own_durable_outcomes_without_a_duplicate_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var first = new RegisterOrganizationCommand($"race-cuit-one-{suffix}@example.test", "Testing1234!", "Northwind One", "30-12345678-1");
        var second = new RegisterOrganizationCommand($"race-cuit-two-{suffix}@example.test", "Testing1234!", "Northwind Two", "30 12345678 1");
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(first, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(second, barrier)));

        results.ShouldAllBe(result => result.IsSuccess);
        var submissions = await TestApp.ListAsync<RegistrationSubmission>();
        submissions.Count.ShouldBe(2);
        submissions.Count(submission => submission.Outcome == RegistrationSubmissionOutcome.Accepted).ShouldBe(2);
        submissions.Count(submission => submission.CompletedAt.HasValue).ShouldBe(2);
        (await TestApp.CountAsync<PendingRegistrationIntent>()).ShouldBe(2);
        await AssertNoExclusiveClaimAsync();

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.RawTokenAt(0)))).IsSuccess.ShouldBeTrue();
        var loser = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.RawTokenAt(1)));

        loser.IsFailure.ShouldBeTrue();
        loser.Error!.Code.ShouldBe("registration_conflict");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(2, "each proved address keeps the account its proof earned");
        await AssertSettledIntentsAsync(created: 1, conflicted: 1);
        await AssertSingleOrganizationGraphAsync();
    }

    /// <summary>
    /// The authenticated branch is where registration still builds the whole organization in one request, so it
    /// is where a partial failure could still leave an organization without its responsible membership — or leave
    /// the idempotency claim standing over effects that no longer exist, which would make the retry replay an
    /// outcome nobody ever produced.
    /// </summary>
    [Test]
    public async Task Failure_after_the_submission_claim_is_persisted_rolls_back_the_entire_registration_and_a_retry_succeeds_once()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);
        var command = new RegisterOrganizationCommand(email, "Testing1234!", "Northwind Registration", "30-12345678-1");
        TestApp.ForceRegistrationRollbackAfterPersistedEffects();

        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(command));

        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "the rollback takes back what the request created, and the caller's own identity is not that");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "organization.registration.requested").ShouldBe(0);

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        await AssertSinglePendingConfirmationGraphAsync(identityId);
    }

    [Test]
    public async Task Existing_identity_replay_is_neutral_and_does_not_create_an_organization_graph()
    {
        var email = $"existing-{Guid.NewGuid():N}@example.test";
        await TestApp.RunAsUserAsync(email, "Testing1234!", []);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Existing Identity", "30-12345678-1"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull();
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
    }

    /// <summary>
    /// The oracle R6 closes, stated as the pair of requests that produced it.
    /// <para>
    /// The two differ in the one thing a caller chooses — the address — and agree on an occupied CUIT. Answering
    /// the taken address neutrally and the untaken one with a conflict made the status a direct read of whether
    /// that address has an account, which is the question anonymous registration exists not to answer
    /// (IA-REQ-003, SPEC section 6). Both now answer the same way and create nothing.
    /// </para>
    /// </summary>
    [Test]
    public async Task An_occupied_cuit_answers_an_anonymous_caller_the_same_for_a_known_and_an_unknown_address()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"existing-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(OrganizationProfile.Create(tenant, "Existing Organization", NormalizedCuit.From("30-12345678-1")));
        var known = $"known-{Guid.NewGuid():N}@example.test";
        await TestApp.RunAsUserAsync(known, "Testing1234!", []);
        TestApp.SetUserId(null);
        TestApp.SetValidatedOptionalSession(null, null);

        var takenAddress = await TestApp.SendAsync(new RegisterOrganizationCommand(known, "Testing1234!", "Conflicting One", "30-12345678-1"));
        var unknownAddress = await TestApp.SendAsync(new RegisterOrganizationCommand($"unknown-{Guid.NewGuid():N}@example.test", "Testing1234!", "Conflicting Two", "30-12345678-1"));

        takenAddress.IsSuccess.ShouldBeTrue();
        unknownAddress.IsSuccess.ShouldBeTrue("an occupied CUIT must not make the answer depend on the address.");
        (await TestApp.ListAsync<RegistrationSubmission>())
            .ShouldAllBe(submission => submission.Outcome == RegistrationSubmissionOutcome.Accepted && submission.CompletedAt.HasValue);

        // Neutral is not the same as permissive: the CUIT is still unique and neither request created anything.
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
    }

    /// <summary>
    /// Replay keeps the durable outcome the submission recorded, and for an anonymous caller that outcome is now
    /// the neutral one. What replay must never do is answer a second way for the same submission.
    /// </summary>
    [Test]
    public async Task Anonymous_occupied_cuit_replay_stays_neutral_and_creates_nothing()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"existing-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(OrganizationProfile.Create(tenant, "Existing Organization", NormalizedCuit.From("30-12345678-1")));
        var command = new RegisterOrganizationCommand($"conflict-{Guid.NewGuid():N}@example.test", "Testing1234!", "Conflicting Organization", "30-12345678-1");

        var first = await TestApp.SendAsync(command);
        var replay = await TestApp.SendAsync(command);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull();
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.Accepted);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
    }

    /// <summary>
    /// A signed-in caller may register only for the address their own session proves, so nothing they can vary
    /// asks about anyone else — and the conflict is the answer they can act on. It is kept, and it still replays.
    /// </summary>
    [Test]
    public async Task Authenticated_occupied_cuit_returns_the_conflict_and_replays_it()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"existing-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(OrganizationProfile.Create(tenant, "Existing Organization", NormalizedCuit.From("30-12345678-1")));
        var email = $"member-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);
        var command = new RegisterOrganizationCommand(email, "Testing1234!", "Conflicting Organization", "30-12345678-1");

        var first = await TestApp.SendAsync(command);
        var replay = await TestApp.SendAsync(command);

        first.IsFailure.ShouldBeTrue();
        first.Error!.Code.ShouldBe("registration_conflict");
        replay.IsFailure.ShouldBeTrue();
        replay.Error!.Code.ShouldBe("registration_conflict");
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull();
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.RegistrationConflict);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
    }

    [Test]
    public async Task Different_normalized_caller_scopes_have_distinct_claims_without_duplicating_an_existing_identity_graph()
    {
        var command = NewCommand();
        var identityId = await TestApp.RunAsUserAsync(command.Email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, command.Email.ToUpperInvariant());

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();

        TestApp.SetValidatedOptionalSession(null, null);
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();

        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(2);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
    }

    [Test]
    public async Task Trusted_matching_session_succeeds_but_mismatched_and_invalid_sessions_are_safe_failures()
    {
        var email = $"session-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);

        (await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Trusted Session", "30-12345678-1"))).IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single();
        membership.IdentityId.ShouldBe(identityId);
        await AssertResponsibleOwnerRoleAsync(identityId);

        TestApp.SetValidatedOptionalSession(identityId, "other@example.test");
        var mismatch = await TestApp.SendAsync(NewCommand());
        mismatch.IsFailure.ShouldBeTrue();
        mismatch.Error!.Code.ShouldBe("invalid_registration");

        TestApp.SetValidatedOptionalSession(null, null, isInvalid: true);
        var invalid = await TestApp.SendAsync(NewCommand());
        invalid.IsFailure.ShouldBeTrue();
        invalid.Error!.Code.ShouldBe("invalid_session");
        ApiProblemDetailsMapper.GetStatusCode(invalid.Error.Category).ShouldBe(StatusCodes.Status401Unauthorized);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1, "an invalid supplied session must not fall back to anonymous registration");

        TestApp.SetValidatedOptionalSession(identityId, null);
        var partial = await TestApp.SendAsync(NewCommand());
        partial.IsFailure.ShouldBeTrue();
        partial.Error!.Code.ShouldBe("invalid_session");
        ApiProblemDetailsMapper.GetStatusCode(partial.Error.Category).ShouldBe(StatusCodes.Status401Unauthorized);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1, "a partial supplied session must not fall back to anonymous registration");

        TestApp.SetValidatedOptionalSession(null, email);
        var partialEmail = await TestApp.SendAsync(NewCommand());
        partialEmail.IsFailure.ShouldBeTrue();
        partialEmail.Error!.Code.ShouldBe("invalid_session");
        ApiProblemDetailsMapper.GetStatusCode(partialEmail.Error.Category).ShouldBe(StatusCodes.Status401Unauthorized);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1, "a partial supplied email must not fall back to anonymous registration");
    }

    /// <summary>
    /// Password policy depends on the submitted password alone, so it is decided before any address is looked up.
    /// The field-indexed refusal must therefore be identical for a taken and a free address; otherwise a caller
    /// could deliberately submit a weak password and use the answer as an address-existence oracle.
    /// </summary>
    [Test]
    public async Task A_password_that_violates_the_policy_is_refused_whether_or_not_the_address_is_taken()
    {
        var takenEmail = $"taken-{Guid.NewGuid():N}@example.test";
        await TestApp.RunAsUserAsync(takenEmail, "Testing1234!", []);
        var freeEmail = $"free-{Guid.NewGuid():N}@example.test";

        var free = await TestApp.SendAsync(new RegisterOrganizationCommand(freeEmail, "short", "Northwind Free", "30-12345678-1"));
        var taken = await TestApp.SendAsync(new RegisterOrganizationCommand(takenEmail, "short", "Northwind Taken", "30-87654321-0"));

        free.IsFailure.ShouldBeTrue();
        free.Error!.Code.ShouldBe("validation_failed");
        free.Error.Category.ShouldBe(ApplicationErrorCategory.Validation);
        free.Error.ValidationErrors.Keys.ShouldBe(["password"]);
        free.Error.ValidationErrors["password"].ShouldBe([
            "Passwords must be at least 12 characters.",
            "Passwords must have at least one non alphanumeric character.",
            "Passwords must have at least one digit ('0'-'9').",
            "Passwords must have at least one uppercase ('A'-'Z')."
        ]);
        taken.IsFailure.ShouldBeTrue("a weak password must not double as an address-existence oracle");
        taken.Error!.Code.ShouldBe(free.Error.Code);
        taken.Error.Category.ShouldBe(free.Error.Category);
        taken.Error.ValidationErrors.Keys.ShouldBe(free.Error.ValidationErrors.Keys);
        taken.Error.ValidationErrors["password"].ShouldBe(free.Error.ValidationErrors["password"]);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0, "neither refusal creates an organization");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "the only identity is the one seeded before the test");
    }

    /// <summary>
    /// The converse case, so the refusal above is not simply "registration always fails": a valid password stays
    /// neutral across the same two addresses. Each request records its own accepted submission, and what it leaves
    /// behind is one intent either way — the account only one of the addresses has is not something an unproved
    /// request may act on, or be seen to have acted on.
    /// </summary>
    [Test]
    public async Task A_valid_password_stays_neutral_whether_or_not_the_address_is_taken()
    {
        var takenEmail = $"taken-{Guid.NewGuid():N}@example.test";
        await TestApp.RunAsUserAsync(takenEmail, "Testing1234!", []);
        var freeEmail = $"free-{Guid.NewGuid():N}@example.test";

        var free = await TestApp.SendAsync(new RegisterOrganizationCommand(freeEmail, "Testing1234!", "Northwind Free", "30-12345678-1"));
        var taken = await TestApp.SendAsync(new RegisterOrganizationCommand(takenEmail, "Testing1234!", "Northwind Taken", "30-87654321-0"));

        free.IsSuccess.ShouldBeTrue();
        taken.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<RegistrationSubmission>())
            .ShouldAllBe(submission => submission.Outcome == RegistrationSubmissionOutcome.Accepted && submission.CompletedAt.HasValue);
        (await TestApp.CountAsync<PendingRegistrationIntent>()).ShouldBe(2);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(2);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0, "neither acceptance produces an organization graph");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "the only identity is the one seeded before the test");
    }

    private static RegisterOrganizationCommand NewCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new RegisterOrganizationCommand($"owner-{suffix}@example.test", "Testing1234!", "Northwind Registration", "30-12345678-1");
    }

    private static async Task<Result> SendFromIndependentScopeAsync(RegisterOrganizationCommand command, Barrier barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await sender.Send(command);
    }

    /// <summary>
    /// Everything one accepted anonymous initiation may leave behind: the recorded submission, the single intent
    /// that request can finalize, the message carrying its proof, and no exclusive claim of any kind. The token is
    /// asserted absent from every column it passes through here rather than after finalization, because
    /// consumption clears the ciphertext and a cleared column cannot show that it never held the token.
    /// </summary>
    private static async Task AssertSingleRegistrationIntentAsync()
    {
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull("the neutral registration result must be recorded before replay is accepted");
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.Accepted);

        var intent = (await TestApp.ListAsync<PendingRegistrationIntent>()).Single();
        intent.SubmissionId.ShouldBe(submission.Id);
        intent.Outcome.ShouldBeNull("an initiation nobody has proved yet decides nothing");
        intent.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        var outbox = (await TestApp.ListAsync<OutboxMessage>()).Single();
        outbox.Type.ShouldBe(RegisterOrganizationCommandHandler.IntentConfirmationMessageType);
        outbox.Payload.ShouldNotContain(TestApp.GetRegistrationRawToken());
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secret.VersionedHash.ShouldNotContain(TestApp.GetRegistrationRawToken());
        secret.Ciphertext!.ShouldNotContain(TestApp.GetRegistrationRawToken());
        secret.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "organization.registration.intent.created").ShouldBe(1);
        await AssertNoExclusiveClaimAsync();
    }

    /// <summary>The claims an initiation may not make, whatever the system knows about the address or the CUIT.</summary>
    private static async Task AssertNoExclusiveClaimAsync()
    {
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(0);
    }

    /// <summary>
    /// How the spent intents ended. Each spend records one terminal outcome, so a settled row is the evidence
    /// that a later replay cannot decide the same registration a second way.
    /// </summary>
    private static async Task AssertSettledIntentsAsync(int created, int conflicted)
    {
        var intents = await TestApp.ListAsync<PendingRegistrationIntent>();
        intents.Count(intent => intent.Outcome == PendingRegistrationIntentOutcome.Created).ShouldBe(created);
        intents.Count(intent => intent.Outcome == PendingRegistrationIntentOutcome.Conflicted).ShouldBe(conflicted);
    }

    /// <summary>One proved journey, end to end: one confirmed owner and the single organization they registered.</summary>
    private static async Task AssertSingleOwnerOrganizationGraphAsync()
    {
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeTrue();
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        await AssertSingleOrganizationGraphAsync();
    }

    private static async Task AssertSingleOrganizationGraphAsync()
    {
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1);
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single();
        await AssertResponsibleOwnerRoleAsync(membership.IdentityId);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "organization.registration.requested").ShouldBe(1);
    }

    /// <summary>
    /// The graph the authenticated branch still creates and mails a confirmation for: the organization exists but
    /// waits, so nothing it holds is usable until the address answers.
    /// </summary>
    private static async Task AssertSinglePendingConfirmationGraphAsync(Guid identityId)
    {
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull("the registration result must be recorded before replay is accepted");
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.Accepted);
        (await TestApp.ListAsync<Tenant>()).Single().Status.ShouldBe(TenantStatus.PendingConfirmation);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.PendingConfirmation);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        await AssertResponsibleOwnerRoleAsync(identityId);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "organization.registration.requested").ShouldBe(1);
    }

    private static async Task AssertResponsibleOwnerRoleAsync(Guid identityId)
    {
        var tenant = (await TestApp.ListAsync<Tenant>()).Single();
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single();
        var owner = (await TestApp.ListAsync<Role>()).Single();
        var assignment = (await TestApp.ListAsync<MembershipRole>()).Single();

        membership.TenantId.ShouldBe(tenant.Id);
        membership.IdentityId.ShouldBe(identityId);
        owner.TenantId.ShouldBe(tenant.Id);
        owner.Name.ShouldBe("Owner");
        owner.NormalizedName.ShouldBe("OWNER");
        owner.IsSystem.ShouldBeTrue();
        owner.IsRetired.ShouldBeFalse();
        assignment.TenantId.ShouldBe(tenant.Id);
        assignment.MembershipId.ShouldBe(membership.Id);
        assignment.RoleId.ShouldBe(owner.Id);
    }
}
