using System.Net;
using System.Text.Json;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;
using CleanArchitecture.Application.IdentityAccess.People.Profile;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.People;

/// <summary>
/// A person's own context, end to end through the request pipeline: the anonymous signup that reserves nothing, the
/// delivered confirmation that creates the whole graph, the already-registered identity that adds Personal without a
/// second credential, and what a profile will and will not say (IA-REQ-050, IA-REQ-056, IA-REQ-057).
/// </summary>
public sealed class PersonalJourneyTests : TestBase
{
    private const string Password = "Testing1234!";

    [Test]
    public async Task An_anonymous_signup_reserves_nothing_and_leaves_the_same_world_for_a_known_and_an_unknown_address()
    {
        var known = await IdentityHttpHarness.SeedConfirmedUserAsync("known@example.test", Password, "en");
        TestApp.SetRequestLanguage("es");

        var unknownResult = await TestApp.SendAsync(new RegisterPersonalCommand("nobody@example.test", Password, "Jane Doe", "Jane", "12345678"));
        var knownResult = await TestApp.SendAsync(new RegisterPersonalCommand("known@example.test", Password, "Jane Doe", "Jane", "23456789"));

        unknownResult.IsSuccess.ShouldBeTrue();
        knownResult.IsSuccess.ShouldBeTrue("a neutral answer is the same answer either way");

        var intents = await TestApp.ListAsync<PendingPersonalIntent>();
        intents.Count.ShouldBe(2);
        intents.ShouldAllBe(intent => intent.Language == "es", "the trusted request language is captured in both neutral branches");
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(2);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "only the identity seeded before the probes exists");
        (await TestApp.FindAsync<ApplicationUser>(known))!.PreferredLanguage.ShouldBe("en", "a neutral existing-account branch never overwrites its preference");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocument>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocumentFingerprint>()).ShouldBe(0, "an unproved request reserves no documentary identity");
        known.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task A_delivered_confirmation_creates_the_whole_personal_context_once()
    {
        TestApp.SetRequestLanguage("es");
        (await TestApp.SendAsync(new RegisterPersonalCommand("jane@example.test", Password, " Jane Doe ", " Jane ", "12.345.678"))).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<PendingPersonalIntent>()).Single().Language.ShouldBe("es");

        var confirmed = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        confirmed.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single();
        identity.EmailConfirmed.ShouldBeTrue();
        identity.PreferredLanguage.ShouldBe("es", "first account creation inherits the immutable personal-intent snapshot");
        var tenant = (await TestApp.ListAsync<Tenant>()).Single();
        tenant.Type.ShouldBe(TenantType.Personal);
        tenant.Status.ShouldBe(TenantStatus.Active);
        (await TestApp.ListAsync<PersonalTenantOwnership>()).Single().IdentityId.ShouldBe(identity.Id);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.Active);
        var profile = (await TestApp.ListAsync<PersonProfile>()).Single();
        profile.FullName.ShouldBe("Jane Doe");
        profile.DisplayName.ShouldBe("Jane");
        profile.Classification.ShouldBe(DataClassification.Synthetic);
        (await TestApp.CountAsync<IdentityDocument>()).ShouldBe(1);
        (await TestApp.CountAsync<IdentityDocumentFingerprint>()).ShouldBeGreaterThan(0);

        var replay = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));
        replay.IsSuccess.ShouldBeTrue("spending a spent token answers what it settled as");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(1);
    }

    [Test]
    public async Task An_expired_personal_confirmation_expires_its_intent_and_remains_an_invalid_confirmation_on_replay()
    {
        (await TestApp.SendAsync(new RegisterPersonalCommand(
            "expired-personal@example.test",
            Password,
            "Jane Doe",
            "Jane",
            "12345678"))).IsSuccess.ShouldBeTrue();
        var token = TestApp.GetRegistrationRawToken();
        await TestApp.ExpireConfirmationSecretAsync();

        var first = await TestApp.SendAsync(new ConfirmEmailCommand(token));
        var replay = await TestApp.SendAsync(new ConfirmEmailCommand(token));

        first.IsFailure.ShouldBeTrue();
        first.Error!.Code.ShouldBe("invalid_confirmation");
        replay.IsFailure.ShouldBeTrue();
        replay.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Expired);
        (await TestApp.ListAsync<PendingPersonalIntent>()).Single().Outcome.ShouldBe(PendingRegistrationIntentOutcome.Expired);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<PersonalTenantOwnership>()).ShouldBe(0);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocument>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocumentFingerprint>()).ShouldBe(0);
        (await TestApp.ListAsync<AuditEvent>()).ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_failed_personal_confirmation_is_invalid_over_http_and_preserves_the_pending_intent()
    {
        (await TestApp.SendAsync(new RegisterPersonalCommand(
            "failed-personal@example.test",
            Password,
            "Jane Doe",
            "Jane",
            "12345678"))).IsSuccess.ShouldBeTrue();
        var token = TestApp.GetRegistrationRawToken();
        var message = (await TestApp.ListAsync<OutboxMessage>())
            .Single(item => item.Type == "identity.personal.confirmation.requested");
        await TestApp.FailConfirmationSecretAsync(message.Id);
        var before = await ReadPersonalConfirmationStateAsync();
        before.IntentOutcome.ShouldBeNull("a delivery failure does not settle an unproved personal intent");
        before.SecretStatus.ShouldBe(OutboxSecretStatus.Failed);
        before.Users.ShouldBe(0);
        before.Tenants.ShouldBe(0);
        before.Memberships.ShouldBe(0);
        before.Ownerships.ShouldBe(0);
        before.Profiles.ShouldBe(0);
        before.Documents.ShouldBe(0);
        before.Fingerprints.ShouldBe(0);

        var host = $"https://failed-personal-confirmation-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/confirm-email",
            new { token },
            antiforgery);
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var problem = await IdentityHttpHarness.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_confirmation");
        problem.GetProperty("instance").GetString().ShouldBe("/api/identity/confirm-email");
        (await ReadPersonalConfirmationStateAsync()).ShouldBe(before,
            "a pre-existing Failed secret must not settle the intent or create identity, graph, or audit effects");
        (await TestApp.ListAsync<AuditEvent>()).ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_conflicted_personal_confirmation_replays_the_recorded_problem_without_duplicate_effects()
    {
        const string email = "conflicted-personal@example.test";
        const string documentNumber = "12345678";
        var host = $"https://personal-conflict-{Guid.NewGuid():N}.localhost";

        (await TestApp.SendAsync(new RegisterPersonalCommand(email, "Different1234!", "Jane Doe", "Jane", documentNumber))).IsSuccess.ShouldBeTrue();
        var token = TestApp.GetRegistrationRawToken();
        await PersonalScenario.SeedConfirmedIdentityAsync(email);
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);

        using var firstRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/confirm-email",
            new { token },
            antiforgery);
        using var firstResponse = await FunctionalTestSetup.HttpClient.SendAsync(firstRequest);
        var firstProblem = await AssertPersonalRegistrationConflictAsync(firstResponse, email, documentNumber, token);
        var durableAfterFirst = await ReadPersonalConfirmationStateAsync();
        durableAfterFirst.ShouldBe(new PersonalConfirmationState(
            Users: 1,
            Submissions: 1,
            Intents: 1,
            IntentOutcome: PendingRegistrationIntentOutcome.Conflicted,
            Messages: 1,
            Secrets: 1,
            SecretStatus: OutboxSecretStatus.Consumed,
            Audits: 2,
            Tenants: 0,
            Memberships: 0,
            Ownerships: 0,
            Profiles: 0,
            Documents: 0,
            Fingerprints: 0));

        using var replayRequest = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/confirm-email",
            new { token },
            antiforgery);
        using var replayResponse = await FunctionalTestSetup.HttpClient.SendAsync(replayRequest);
        var replayProblem = await AssertPersonalRegistrationConflictAsync(replayResponse, email, documentNumber, token);

        replayProblem.GetProperty("detail").GetString().ShouldBe(firstProblem.GetProperty("detail").GetString(),
            "the replay must return the same collapsed refusal the first spend recorded");
        (await ReadPersonalConfirmationStateAsync()).ShouldBe(durableAfterFirst,
            "a replay must not create another identity graph, message, secret or audit effect");
    }

    [Test]
    public async Task An_already_registered_identity_adds_personal_without_a_second_credential()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("member@example.test");
        var organization = await PersonalScenario.SeedOrganizationForAsync(identityId);
        PersonalScenario.RunAs(identityId);
        var hashesBefore = TestApp.PasswordVerificationCount;

        var result = await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "the person keeps the one account they already had");
        TestApp.PasswordVerificationCount.ShouldBe(hashesBefore, "adding a context asks for no credential");
        var tenants = await TestApp.ListAsync<Tenant>();
        tenants.Count.ShouldBe(2);
        tenants.Count(tenant => tenant.Type == TenantType.Personal).ShouldBe(1);
        tenants.ShouldContain(tenant => tenant.Id == organization);
        (await TestApp.ListAsync<PersonProfile>()).Single().IdentityId.ShouldBe(identityId);
        (await TestApp.ListAsync<PersonalTenantOwnership>()).Single().IdentityId.ShouldBe(identityId);
    }

    [Test]
    public async Task Signed_in_personal_creation_refuses_each_invalid_field_with_exact_validation_and_no_effects()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("invalid-personal@example.test");
        await PersonalScenario.RunWithSessionAsync(identityId);
        TestApp.SetHttpAuthorizationGranted(true);
        var host = $"https://personal-validation-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);

        var before = new[] {
            await TestApp.CountAsync<Tenant>(),
            await TestApp.CountAsync<PersonProfile>(),
            await TestApp.CountAsync<IdentityDocument>()
        };

        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = " ", displayName = "Jane", documentNumber = "12345678"
            }, antiforgery),
            "/api/identity/personal", "fullName", ValidationDetail(ValidationErrorCodes.Required), "invalid-personal-value");
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = new string('f', 201), displayName = "Jane", documentNumber = "12345678"
            }, antiforgery),
            "/api/identity/personal", "fullName", ValidationDetail(ValidationErrorCodes.TooLong, 200), new string('f', 201));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = "Jane Doe", displayName = " ", documentNumber = "12345678"
            }, antiforgery),
            "/api/identity/personal", "displayName", ValidationDetail(ValidationErrorCodes.Required));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = "Jane Doe", displayName = new string('d', 61), documentNumber = "12345678"
            }, antiforgery),
            "/api/identity/personal", "displayName", ValidationDetail(ValidationErrorCodes.TooLong, 60), new string('d', 61));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = "Jane Doe", displayName = "Jane", documentNumber = " "
            }, antiforgery),
            "/api/identity/personal", "documentNumber", ValidationDetail(ValidationErrorCodes.Required));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = "Jane Doe", displayName = "Jane", documentNumber = new string('1', 33)
            }, antiforgery),
            "/api/identity/personal", "documentNumber", ValidationDetail(ValidationErrorCodes.TooLong, 32), new string('1', 33));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Post, host, "/api/identity/personal", new
            {
                fullName = "Jane Doe", displayName = "Jane", documentNumber = "12x"
            }, antiforgery),
            "/api/identity/personal", "documentNumber", ValidationDetail(ValidationErrorCodes.Invalid), "12x");

        new[] {
            await TestApp.CountAsync<Tenant>(),
            await TestApp.CountAsync<PersonProfile>(),
            await TestApp.CountAsync<IdentityDocument>()
        }.ShouldBe(before, "validation must run before any personal-context effect");
    }

    [Test]
    public async Task A_duplicate_document_and_an_identity_that_already_owns_a_personal_context_answer_the_same_conflict()
    {
        var first = await PersonalScenario.SeedConfirmedIdentityAsync("first@example.test");
        PersonalScenario.RunAs(first);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("First Person", "First", "12345678"))).IsSuccess.ShouldBeTrue();

        var duplicateDocument = await TestApp.SendAsync(new CreatePersonalContextCommand("First Person", "First", "23456789"));

        var second = await PersonalScenario.SeedConfirmedIdentityAsync("second@example.test");
        PersonalScenario.RunAs(second);
        var takenNumber = await TestApp.SendAsync(new CreatePersonalContextCommand("Second Person", "Second", "12345678"));

        duplicateDocument.IsSuccess.ShouldBeFalse();
        takenNumber.IsSuccess.ShouldBeFalse();
        takenNumber.Error!.Code.ShouldBe(duplicateDocument.Error!.Code, "one refusal for both, or the pair is an oracle for whose document is recorded");
        takenNumber.Error!.Code.ShouldBe("personal_registration_conflict");
        takenNumber.Error!.Detail.ShouldBe(duplicateDocument.Error!.Detail);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(1, "neither refusal created anything");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
    }

    [Test]
    public async Task The_document_claim_is_bounded_and_the_attempt_after_the_budget_is_refused_as_rate_limited()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("busy@example.test");
        var other = await PersonalScenario.SeedConfirmedIdentityAsync("holder@example.test");
        PersonalScenario.RunAs(other);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Holder", "Holder", "12345678"))).IsSuccess.ShouldBeTrue();

        PersonalScenario.RunAs(identityId);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var refused = await TestApp.SendAsync(new CreatePersonalContextCommand("Busy Person", "Busy", "12345678"));
            refused.Error!.Code.ShouldBe("personal_registration_conflict", $"attempt {attempt} is inside the budget and meets the ordinary refusal");
        }

        var overBudget = await TestApp.SendAsync(new CreatePersonalContextCommand("Busy Person", "Busy", "12345678"));

        overBudget.Error!.Code.ShouldBe("rate_limit_exceeded");
        overBudget.Error!.RetryAfterSeconds.ShouldNotBeNull();
    }

    [Test]
    public async Task An_unreachable_budget_store_refuses_the_claim_as_an_outage_rather_than_as_too_many_attempts()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("outage@example.test");
        PersonalScenario.RunAs(identityId);
        TestApp.ForceAttemptBudgetUnavailable();

        var result = await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"));

        result.Error!.Code.ShouldBe("service_unavailable");
        result.Error!.RetryAfterSeconds.ShouldBe(30);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(0, "failing closed means nothing was claimed");
    }

    [Test]
    public async Task A_profile_reads_masked_and_offers_the_correction_this_increment_can_serve()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("reader@example.test");
        PersonalScenario.RunAs(identityId);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();

        var profile = await TestApp.SendAsync(new GetPersonalProfileQuery());

        profile.IsSuccess.ShouldBeTrue();
        profile.Value!.FullName.ShouldBe("Jane Doe");
        profile.Value!.Email.ShouldBe("reader@example.test");
        profile.Value!.Document.ShouldNotBeNull();
        profile.Value!.Document!.MaskedNumber.ShouldBe("••••••78");
        profile.Value!.Document!.Country.ShouldBe("AR");
        profile.Value!.Document!.Type.ShouldBe("DNI");
        // Task 26 built the dispute route this test used to say did not exist. What `correctionAvailable` now
        // reports is whether *this person* may open one, and with none open they may (IA-REQ-058).
        profile.Value!.Document!.CorrectionAvailable.ShouldBeTrue("the dispute route exists and this person has none open");
        profile.Value!.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task A_profile_edit_changes_the_two_permitted_names_and_nothing_else()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("editor@example.test");
        PersonalScenario.RunAs(identityId);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();
        var before = (await TestApp.SendAsync(new GetPersonalProfileQuery())).Value!;

        var updated = await TestApp.SendAsync(new UpdatePersonalProfileCommand("Jane Q. Doe", "Janie", before.Version));

        updated.IsSuccess.ShouldBeTrue();
        updated.Value!.FullName.ShouldBe("Jane Q. Doe");
        updated.Value!.DisplayName.ShouldBe("Janie");
        updated.Value!.Document!.MaskedNumber.ShouldBe(before.Document!.MaskedNumber, "an edit never touches the document");
        updated.Value!.Email.ShouldBe(before.Email);
        updated.Value!.PersonalTenantId.ShouldBe(before.PersonalTenantId);

        var stale = await TestApp.SendAsync(new UpdatePersonalProfileCommand("Someone Else", "Else", before.Version));
        stale.IsSuccess.ShouldBeFalse();
        stale.Error!.Code.ShouldBe("personal_profile_concurrency_conflict");
    }

    [Test]
    public async Task Profile_update_separates_malformed_input_stale_versions_and_unknown_members()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("profile-validation@example.test");
        await PersonalScenario.RunWithSessionAsync(identityId);
        TestApp.SetHttpAuthorizationGranted(true);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();
        var original = (await TestApp.SendAsync(new GetPersonalProfileQuery())).Value!;
        var host = $"https://profile-validation-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);

        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = " ", displayName = "Jane", version = original.Version
            }, antiforgery),
            "/api/identity/profile", "fullName", ValidationDetail(ValidationErrorCodes.Required), "forbidden-full-name");
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = new string('f', 201), displayName = "Jane", version = original.Version
            }, antiforgery),
            "/api/identity/profile", "fullName", ValidationDetail(ValidationErrorCodes.TooLong, 200), new string('f', 201));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = "Jane Doe", displayName = " ", version = original.Version
            }, antiforgery),
            "/api/identity/profile", "displayName", ValidationDetail(ValidationErrorCodes.Required));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = "Jane Doe", displayName = new string('d', 61), version = original.Version
            }, antiforgery),
            "/api/identity/profile", "displayName", ValidationDetail(ValidationErrorCodes.TooLong, 60), new string('d', 61));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = "Jane Doe", displayName = "Jane", version = " "
            }, antiforgery),
            "/api/identity/profile", "version", ValidationDetail(ValidationErrorCodes.Required));
        await AssertFieldValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = "Jane Doe", displayName = "Jane", version = "01"
            }, antiforgery),
            "/api/identity/profile", "version", ValidationDetail(ValidationErrorCodes.Invalid), "01");

        await AssertBindingValidationAsync(
            await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
            {
                fullName = 42, displayName = "binding-profile-sentinel", version = original.Version
            }, antiforgery),
            "/api/identity/profile", "binding-profile-sentinel");

        var moved = await TestApp.SendAsync(new UpdatePersonalProfileCommand("Jane Q. Doe", "Janie", original.Version));
        moved.IsSuccess.ShouldBeTrue();
        using var stale = await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new
        {
            fullName = "Someone Else", displayName = "Else", version = original.Version
        }, antiforgery);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(stale)).GetProperty("code").GetString()
            .ShouldBe("personal_profile_concurrency_conflict");

        using var unknown = await SendPersonalJsonAsync(HttpMethod.Put, host, "/api/identity/profile", new Dictionary<string, object?>
        {
            ["fullName"] = "Jane Q. Doe",
            ["displayName"] = "Janie",
            ["version"] = moved.Value!.Version,
            ["zzzUnknown"] = "never-echo-this-value",
            ["aaaUnknown"] = "also-never-echo-this-value"
        }, antiforgery);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var raw = await unknown.Content.ReadAsStringAsync();
        var unknownProblem = JsonDocument.Parse(raw).RootElement;
        unknownProblem.GetProperty("code").GetString().ShouldBe("profile_field_not_editable");
        unknownProblem.GetProperty("detail").GetString().ShouldBe("The member 'aaaUnknown' cannot be edited through this request.");
        raw.ShouldNotContain("never-echo-this-value");
        raw.ShouldNotContain("also-never-echo-this-value");
        (await TestApp.SendAsync(new GetPersonalProfileQuery())).Value!.FullName.ShouldBe("Jane Q. Doe");
    }

    [Test]
    public async Task An_identity_with_no_personal_context_is_told_nothing_exists_rather_than_shown_somebody_elses()
    {
        var owner = await PersonalScenario.SeedConfirmedIdentityAsync("owner@example.test");
        PersonalScenario.RunAs(owner);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();

        var stranger = await PersonalScenario.SeedConfirmedIdentityAsync("stranger@example.test");
        PersonalScenario.RunAs(stranger);

        var profile = await TestApp.SendAsync(new GetPersonalProfileQuery());

        profile.IsSuccess.ShouldBeFalse();
        profile.Error!.Code.ShouldBe("personal_profile_not_found");
    }

    [Test]
    public async Task Nothing_a_person_typed_reaches_an_audit_record_an_outbox_payload_or_a_log()
    {
        TestApp.ResetCapturedLogs();
        (await TestApp.SendAsync(new RegisterPersonalCommand("quiet@example.test", Password, "Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();

        foreach (var message in await TestApp.ListAsync<OutboxMessage>())
        {
            message.Payload.Contains("12345678", StringComparison.Ordinal).ShouldBeFalse();
            message.Payload.Contains("quiet@example.test", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }

        foreach (var audit in await TestApp.ListAsync<AuditEvent>())
        {
            foreach (var value in audit.Metadata.Values)
            {
                value.Contains("12345678", StringComparison.Ordinal).ShouldBeFalse();
                value.Contains("quiet@example.test", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
            }
        }

        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains("12345678", StringComparison.Ordinal));
        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains(Password, StringComparison.Ordinal));
    }

    [Test]
    public async Task Switching_between_personal_and_organization_never_mixes_their_permissions()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("switcher@example.test");
        var organization = await PersonalScenario.SeedOrganizationWithPermissionsAsync(identityId, Permissions.MembersRead);
        await PersonalScenario.RunWithSessionAsync(identityId);
        (await TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678"))).IsSuccess.ShouldBeTrue();
        var personal = (await TestApp.ListAsync<Tenant>()).Single(tenant => tenant.Type == TenantType.Personal);

        var inOrganization = await TestApp.SendAsync(new SelectTenantCommand(organization));
        var inPersonal = await TestApp.SendAsync(new SelectTenantCommand(personal.Id));
        var backInOrganization = await TestApp.SendAsync(new SelectTenantCommand(organization));

        inOrganization.Value!.Permissions.ShouldContain(Permissions.MembersRead);
        inPersonal.Value!.ActiveTenant!.Type.ShouldBe(nameof(TenantType.Personal));
        inPersonal.Value!.Permissions.ShouldNotContain(Permissions.MembersRead, "permissions belong to one membership, never to the identity");
        backInOrganization.Value!.Permissions.ShouldContain(Permissions.MembersRead, "switching back restores what that membership grants");
        inPersonal.Value!.DisplayName.ShouldBe("Jane", "a person who told us their name is called by it rather than by their address");
        inPersonal.Value!.PersonalDataMode.ShouldBe("Synthetic");
    }

    [Test]
    public async Task Concurrent_creations_by_one_identity_leave_exactly_one_personal_context()
    {
        var identityId = await PersonalScenario.SeedConfirmedIdentityAsync("racer@example.test");
        PersonalScenario.RunAs(identityId);

        var attempts = await Task.WhenAll(
            TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678")),
            TestApp.SendAsync(new CreatePersonalContextCommand("Jane Doe", "Jane", "12345678")));

        attempts.Count(attempt => attempt.IsSuccess).ShouldBe(1, "one winner");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(1);
        (await TestApp.CountAsync<PersonalTenantOwnership>()).ShouldBe(1);
        (await TestApp.CountAsync<IdentityDocument>()).ShouldBe(1);
    }

    private static async Task<JsonElement> AssertPersonalRegistrationConflictAsync(
        HttpResponseMessage response,
        string email,
        string documentNumber,
        string token)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await IdentityHttpHarness.ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.Conflict);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
        problem.GetProperty("title").GetString().ShouldBe("Conflict");
        problem.GetProperty("detail").GetString().ShouldBe("The personal context cannot be created in its current state.");
        problem.GetProperty("instance").GetString().ShouldBe("/api/identity/confirm-email");
        problem.GetProperty("code").GetString().ShouldBe("personal_registration_conflict");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.TryGetProperty("success", out _).ShouldBeFalse();
        problem.TryGetProperty("data", out _).ShouldBeFalse();
        problem.TryGetProperty("error", out _).ShouldBeFalse();
        problem.TryGetProperty("errors", out _).ShouldBeFalse();
        var payload = problem.GetRawText();
        payload.Contains(email, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        payload.Contains(documentNumber, StringComparison.Ordinal).ShouldBeFalse();
        payload.Contains(token, StringComparison.Ordinal).ShouldBeFalse();
        return problem;
    }

    private static async Task<HttpResponseMessage> SendPersonalJsonAsync(
        HttpMethod method,
        string host,
        string path,
        object body,
        string antiforgery)
    {
        using var request = IdentityHttpHarness.JsonRequest(method, $"{host}{path}", body, antiforgery);
        return await FunctionalTestSetup.HttpClient.SendAsync(request);
    }

    private static async Task AssertFieldValidationAsync(
        HttpResponseMessage response,
        string instance,
        string field,
        ValidationErrorDetail expectedDetail,
        params string[] submittedValues)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            var raw = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(raw);
            var problem = document.RootElement;
            problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
            problem.GetProperty("type").GetString().ShouldBe("about:blank");
            problem.GetProperty("title").GetString().ShouldBe("Bad Request");
            problem.GetProperty("instance").GetString().ShouldBe(instance);
            problem.GetProperty("code").GetString().ShouldBe("validation_failed");
            AssertSafeTraceId(problem);
            var errors = problem.GetProperty("errors");
            errors.EnumerateObject().Select(property => property.Name).ShouldBe([field]);
            var details = errors.GetProperty(field).EnumerateArray().ToArray();
            details.Length.ShouldBe(1);
            AssertValidationDetail(details[0], expectedDetail);
            AssertDoesNotEchoSubmittedValuesOutsideTraceId(problem, submittedValues);
        }
    }

    private static ValidationErrorDetail ValidationDetail(string code, int? max = null) =>
        new(code, max is null
            ? new Dictionary<string, int>()
            : new Dictionary<string, int> { ["max"] = max.Value });

    private static void AssertValidationDetail(JsonElement actual, ValidationErrorDetail expected)
    {
        actual.ValueKind.ShouldBe(JsonValueKind.Object);
        actual.EnumerateObject().Select(property => property.Name).ShouldBe(["code", "params"]);
        actual.GetProperty("code").GetString().ShouldBe(expected.Code);
        var parameters = actual.GetProperty("params");
        parameters.ValueKind.ShouldBe(JsonValueKind.Object);
        parameters.EnumerateObject().Select(property => property.Name).ShouldBe(expected.Params.Keys);
        foreach (var expectedParameter in expected.Params)
        {
            parameters.GetProperty(expectedParameter.Key).GetInt32().ShouldBe(expectedParameter.Value);
        }
    }

    private static async Task AssertBindingValidationAsync(
        HttpResponseMessage response,
        string instance,
        params string[] submittedValues)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            var raw = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(raw);
            var problem = document.RootElement;
            problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
            problem.GetProperty("type").GetString().ShouldBe("about:blank");
            problem.GetProperty("title").GetString().ShouldBe("Bad Request");
            problem.GetProperty("instance").GetString().ShouldBe(instance);
            problem.GetProperty("code").GetString().ShouldBe("validation_failed");
            AssertSafeTraceId(problem);
            problem.TryGetProperty("errors", out _).ShouldBeFalse("binding failures do not invent application field errors");
            AssertDoesNotEchoSubmittedValuesOutsideTraceId(problem, submittedValues);
        }
    }

    private static void AssertSafeTraceId(JsonElement problem)
    {
        var traceId = problem.GetProperty("traceId").GetString();
        traceId.ShouldNotBeNullOrWhiteSpace();
        traceId!.ShouldMatch("^[0-9a-f]{32}$");
    }

    private static void AssertDoesNotEchoSubmittedValuesOutsideTraceId(
        JsonElement problem,
        params string[] submittedValues)
    {
        foreach (var property in problem.EnumerateObject())
        {
            foreach (var value in submittedValues)
            {
                property.Name.ShouldNotContain(value);
            }

            if (property.NameEquals("traceId")) continue;

            var valueJson = property.Value.GetRawText();
            foreach (var value in submittedValues)
            {
                valueJson.ShouldNotContain(value);
            }
        }
    }

    private static async Task<PersonalConfirmationState> ReadPersonalConfirmationStateAsync()
    {
        var intent = (await TestApp.ListAsync<PendingPersonalIntent>()).Single();
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        return new PersonalConfirmationState(
            await TestApp.CountAsync<ApplicationUser>(),
            await TestApp.CountAsync<RegistrationSubmission>(),
            await TestApp.CountAsync<PendingPersonalIntent>(),
            intent.Outcome,
            await TestApp.CountAsync<OutboxMessage>(),
            await TestApp.CountAsync<OutboxSecret>(),
            secret.Status,
            await TestApp.CountAsync<AuditEvent>(),
            await TestApp.CountAsync<Tenant>(),
            await TestApp.CountAsync<TenantMembership>(),
            await TestApp.CountAsync<PersonalTenantOwnership>(),
            await TestApp.CountAsync<PersonProfile>(),
            await TestApp.CountAsync<IdentityDocument>(),
            await TestApp.CountAsync<IdentityDocumentFingerprint>());
    }

    private sealed record PersonalConfirmationState(
        int Users,
        int Submissions,
        int Intents,
        PendingRegistrationIntentOutcome? IntentOutcome,
        int Messages,
        int Secrets,
        OutboxSecretStatus SecretStatus,
        int Audits,
        int Tenants,
        int Memberships,
        int Ownerships,
        int Profiles,
        int Documents,
        int Fingerprints);
}
