using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;
using CleanArchitecture.Application.IdentityAccess.People.Profile;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
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
        var known = await PersonalScenario.SeedConfirmedIdentityAsync("known@example.test");

        var unknownResult = await TestApp.SendAsync(new RegisterPersonalCommand("nobody@example.test", Password, "Jane Doe", "Jane", "12345678"));
        var knownResult = await TestApp.SendAsync(new RegisterPersonalCommand("known@example.test", Password, "Jane Doe", "Jane", "23456789"));

        unknownResult.IsSuccess.ShouldBeTrue();
        knownResult.IsSuccess.ShouldBeTrue("a neutral answer is the same answer either way");

        (await TestApp.CountAsync<PendingPersonalIntent>()).ShouldBe(2);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(2);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "only the identity seeded before the probes exists");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<PersonProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocument>()).ShouldBe(0);
        (await TestApp.CountAsync<IdentityDocumentFingerprint>()).ShouldBe(0, "an unproved request reserves no documentary identity");
        known.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task A_delivered_confirmation_creates_the_whole_personal_context_once()
    {
        (await TestApp.SendAsync(new RegisterPersonalCommand("jane@example.test", Password, " Jane Doe ", " Jane ", "12.345.678"))).IsSuccess.ShouldBeTrue();

        var confirmed = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        confirmed.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single();
        identity.EmailConfirmed.ShouldBeTrue();
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
}
