using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.Common.Models;
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
    [Test]
    public async Task Normalized_equivalent_anonymous_requests_are_neutral_and_create_one_registration_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var command = new RegisterOrganizationCommand($"owner-{suffix}@example.test", "Testing1234!", "Northwind Registration", "30-12345678-9");
        var normalizedVariant = command with
        {
            Email = $"  OWNER-{suffix.ToUpperInvariant()}@EXAMPLE.TEST ",
            LegalName = "  northwind registration  ",
            Cuit = "30 12345678 9"
        };

        var first = await TestApp.SendAsync(command);
        var second = await TestApp.SendAsync(normalizedVariant);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await AssertSingleRegistrationGraphAsync();
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
        await AssertSingleRegistrationGraphAsync();
    }

    [Test]
    public async Task Concurrent_anonymous_requests_with_different_intents_and_the_same_email_complete_neutrally_without_a_duplicate_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"race-email-{suffix}@example.test";
        var first = new RegisterOrganizationCommand(email, "Testing1234!", "Northwind One", "30-12345678-9");
        var second = new RegisterOrganizationCommand(email.ToUpperInvariant(), "Testing1234!", "Northwind Two", "30-87654321-0");
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(first, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(second, barrier)));

        results.ShouldAllBe(result => result.IsSuccess);
        var submissions = await TestApp.ListAsync<RegistrationSubmission>();
        submissions.Count.ShouldBe(2);
        submissions.Count(submission => submission.CompletedAt.HasValue && submission.Outcome == RegistrationSubmissionOutcome.Accepted).ShouldBe(2);
        await AssertSingleOrganizationGraphAsync();
    }

    [Test]
    public async Task Concurrent_anonymous_requests_with_different_intents_and_the_same_cuit_complete_their_own_durable_outcomes_without_a_duplicate_graph()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var first = new RegisterOrganizationCommand($"race-cuit-one-{suffix}@example.test", "Testing1234!", "Northwind One", "30-12345678-9");
        var second = new RegisterOrganizationCommand($"race-cuit-two-{suffix}@example.test", "Testing1234!", "Northwind Two", "30 12345678 9");
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(first, barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(second, barrier)));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        var conflict = results.Single(result => result.IsFailure);
        conflict.Error!.Code.ShouldBe("registration_conflict");
        var submissions = await TestApp.ListAsync<RegistrationSubmission>();
        submissions.Count.ShouldBe(2);
        submissions.Count(submission => submission.Outcome == RegistrationSubmissionOutcome.Accepted).ShouldBe(1);
        submissions.Count(submission => submission.Outcome == RegistrationSubmissionOutcome.RegistrationConflict).ShouldBe(1);
        submissions.Count(submission => submission.CompletedAt.HasValue).ShouldBe(2);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
    }

    [Test]
    public async Task Failure_after_identity_persistence_rolls_back_the_entire_registration_and_a_retry_succeeds_once()
    {
        var command = NewCommand();
        TestApp.ForceRegistrationRollbackAfterPersistedEffects();

        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(command));

        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "organization.registration.requested").ShouldBe(0);

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        await AssertSingleRegistrationGraphAsync();
    }

    [Test]
    public async Task Existing_identity_replay_is_neutral_and_does_not_create_an_organization_graph()
    {
        var email = $"existing-{Guid.NewGuid():N}@example.test";
        await TestApp.RunAsUserAsync(email, "Testing1234!", []);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Existing Identity", "30-12345678-9"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull();
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
    }

    [Test]
    public async Task Conflict_replay_returns_the_original_conflict_instead_of_a_neutral_success()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"existing-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);
        await TestApp.AddAsync(OrganizationProfile.Create(tenant, "Existing Organization", NormalizedCuit.From("30-12345678-9")));
        var command = new RegisterOrganizationCommand($"conflict-{Guid.NewGuid():N}@example.test", "Testing1234!", "Conflicting Organization", "30-12345678-9");

        var first = await TestApp.SendAsync(command);
        var replay = await TestApp.SendAsync(command);

        first.IsFailure.ShouldBeTrue();
        first.Error!.Code.ShouldBe("registration_conflict");
        replay.IsFailure.ShouldBeTrue();
        replay.Error!.Code.ShouldBe("registration_conflict");
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull();
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.RegistrationConflict);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
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

        (await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Trusted Session", "30-12345678-9"))).IsSuccess.ShouldBeTrue();
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

    private static RegisterOrganizationCommand NewCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new RegisterOrganizationCommand($"owner-{suffix}@example.test", "Testing1234!", "Northwind Registration", "30-12345678-9");
    }

    private static async Task<Result> SendFromIndependentScopeAsync(RegisterOrganizationCommand command, Barrier barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await sender.Send(command);
    }

    private static async Task AssertSingleRegistrationGraphAsync()
    {
        var submission = (await TestApp.ListAsync<RegistrationSubmission>()).Single();
        submission.CompletedAt.ShouldNotBeNull("the neutral registration result must be recorded before replay is accepted");
        submission.Outcome.ShouldBe(RegistrationSubmissionOutcome.Accepted);
        await AssertSingleOrganizationGraphAsync();
    }

    private static async Task AssertSingleOrganizationGraphAsync()
    {
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1);
        var membership = (await TestApp.ListAsync<TenantMembership>()).Single();
        await AssertResponsibleOwnerRoleAsync(membership.IdentityId);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        var outbox = (await TestApp.ListAsync<OutboxMessage>()).Single();
        outbox.Payload.ShouldNotContain(TestApp.GetRegistrationRawToken());
        secret.VersionedHash.ShouldNotContain(TestApp.GetRegistrationRawToken());
        secret.Ciphertext!.ShouldNotContain(TestApp.GetRegistrationRawToken());
        secret.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
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
