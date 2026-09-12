using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

public sealed class ConfirmEmailTests : TestBase
{
    [Test]
    public async Task Valid_confirmation_activates_the_identity_tenant_and_responsible_membership_once_without_exposing_the_token()
    {
        var registration = await RegisterAsync();
        var rawToken = TestApp.GetRegistrationRawToken();

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(rawToken));

        result.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single();
        identity.EmailConfirmed.ShouldBeTrue();
        (await TestApp.ListAsync<Tenant>()).Single().Status.ShouldBe(TenantStatus.Active);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.Active);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(1);
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secret.Status.ShouldBe(OutboxSecretStatus.Consumed);
        secret.Ciphertext.ShouldBeNull();
        secret.ProviderReceipt.ShouldBeNull("consumption before provider delivery must not create partial delivery evidence");
        secret.CompletedAt.ShouldNotBeNull("terminal secret consumption must retain its timestamp");
        secret.VersionedHash.ShouldNotContain(rawToken);
        secret.VersionedHash.ShouldNotContain(registration.Email);
        var outbox = (await TestApp.ListAsync<OutboxMessage>()).Single();
        outbox.Payload.ShouldNotContain(rawToken);
        outbox.Payload.ShouldNotContain(registration.Email);
        outbox.Payload.ShouldNotContain(registration.Cuit);
        var audit = await TestApp.ListAsync<AuditEvent>();
        audit.Count(item => item.EventType == "organization.registration.requested").ShouldBe(1);
        audit.Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
        audit.Single(item => item.EventType == "identity.confirmed").Metadata.Values.ShouldAllBe(value => !value.Contains(rawToken, StringComparison.Ordinal));
        audit.SelectMany(item => item.Metadata.Values).ShouldAllBe(value => !value.Contains(registration.Email, StringComparison.OrdinalIgnoreCase) && !value.Contains(registration.Cuit, StringComparison.Ordinal));
    }

    [Test]
    public async Task Successful_confirmation_replay_is_bodyless_success_without_duplicate_effects()
    {
        await RegisterAsync();
        var command = new ConfirmEmailCommand(TestApp.GetRegistrationRawToken());

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        var consumed = (await TestApp.ListAsync<OutboxSecret>()).Single();
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();

        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
        var replayedSecret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        replayedSecret.Status.ShouldBe(OutboxSecretStatus.Consumed);
        replayedSecret.CompletedAt.ShouldBe(consumed.CompletedAt);
        replayedSecret.ProviderReceipt.ShouldBeNull();
    }

    [Test]
    public async Task Delivered_confirmation_secret_remains_consumable_once_and_preserves_delivery_evidence()
    {
        await RegisterAsync();
        var deliveredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await TestApp.MarkConfirmationSecretDeliveredAsync("provider_delivered", deliveredAt, "provider-receipt");

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();

        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secret.Status.ShouldBe(OutboxSecretStatus.Consumed);
        secret.Ciphertext.ShouldBeNull();
        secret.DeliveredAt.ShouldNotBeNull();
        secret.DeliveredAt.Value.ShouldBeInRange(deliveredAt.AddMilliseconds(-1), deliveredAt.AddMilliseconds(1));
        secret.DeliveryReason.ShouldBe("provider_delivered");
        secret.ProviderReceipt.ShouldBe("provider-receipt");
        secret.CompletedAt.ShouldNotBeNull();
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
    }

    [Test]
    public async Task Parallel_confirmation_requests_lock_the_secret_and_the_loser_replays_the_committed_consumed_state()
    {
        await RegisterAsync();
        TestApp.EnableConfirmationSecretLockBarrier();
        var command = new ConfirmEmailCommand(TestApp.GetRegistrationRawToken());

        var results = await Task.WhenAll(
            Task.Run(() => TestApp.SendAsync(command)),
            Task.Run(() => TestApp.SendAsync(command)));

        results.ShouldAllBe(result => result.IsSuccess);
        TestApp.ConfirmationSecretLockBarrierWasObserved.ShouldBeTrue();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Consumed);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
    }

    [TestCase("")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI==")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI_")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Invalid_token_shapes_are_rejected_without_invoking_the_hasher(string token)
    {
        await RegisterAsync();
        TestApp.ResetConfirmationTokenHashInvocationCount();

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        TestApp.ConfirmationTokenHashInvocationCount.ShouldBe(0);
        await AssertUnspentIntentAsync();
    }

    [TestCase("")]
    [TestCase("wrong-confirmation-token")]
    [TestCase("not-a-valid-token-shape")]
    public async Task Malformed_or_unknown_tokens_are_stable_validation_failures_without_state_change(string token)
    {
        await RegisterAsync();

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        await AssertUnspentIntentAsync();
    }

    [Test]
    public async Task Version_or_hash_mismatch_is_a_stable_validation_failure_without_state_change()
    {
        await RegisterAsync();
        await TestApp.SetConfirmationSecretHashAsync("v2:deliberately-unmatched-versioned-hash");

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        await AssertUnspentIntentAsync();
    }

    /// <summary>
    /// A dead envelope settles the registration it carried. Leaving the intent open would keep a row whose meaning
    /// depends on a clock rather than on a decision, and would let the same expired link go on asking.
    /// </summary>
    [Test]
    public async Task Expired_confirmation_is_rejected_without_activation_or_successful_consumption()
    {
        await RegisterAsync();
        await TestApp.ExpireConfirmationSecretAsync();

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("registration_conflict");
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secret.Status.ShouldBe(OutboxSecretStatus.Expired);
        (await TestApp.ListAsync<PendingRegistrationIntent>()).Single().Outcome.ShouldBe(PendingRegistrationIntentOutcome.Expired);
        await AssertNothingWasCreatedAsync();
    }

    [Test]
    public async Task Expired_pre_upgrade_organization_confirmation_keeps_the_organization_conflict_contract()
    {
        await RegisterAsSignedInCallerAsync();
        await TestApp.ExpireConfirmationSecretAsync();

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("registration_conflict");
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Expired);
        (await TestApp.ListAsync<Tenant>()).Single().Status.ShouldBe(TenantStatus.PendingConfirmation);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.PendingConfirmation);
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeFalse();
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(0);
    }

    [TestCase(TenantStatus.Suspended, "PendingConfirmation")]
    [TestCase(TenantStatus.Closed, "PendingConfirmation")]
    [TestCase(TenantStatus.Active, "Suspended")]
    [TestCase(TenantStatus.Active, "Revoked")]
    public async Task Suspended_or_terminal_lifecycle_states_reject_without_confirmation_or_secret_consumption(TenantStatus tenantStatus, string membershipStatus)
    {
        await RegisterAsSignedInCallerAsync();
        await TestApp.SetConfirmationLifecycleAsync(tenantStatus, membershipStatus);

        var result = await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("registration_conflict");
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Pending);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(0);
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeFalse();
    }

    /// <summary>
    /// Finalization is the request that creates the identity, so a failure inside it must take the identity back
    /// with everything else. An account left standing for an address whose organization never existed would let
    /// the retry find its own half-finished work and refuse the person their own registration.
    /// </summary>
    [Test]
    public async Task Failure_after_identity_activation_rolls_back_every_confirmation_effect_and_retry_succeeds_once()
    {
        await RegisterAsync();
        var command = new ConfirmEmailCommand(TestApp.GetRegistrationRawToken());
        TestApp.ForceConfirmationRollbackAfterPersistedEffects();

        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(command));

        await AssertUnspentIntentAsync();
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(1);
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeTrue();
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(1);
    }

    private static async Task<RegistrationInput> RegisterAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var input = new RegistrationInput($"confirm-{suffix}@example.test", "30-12345678-1");
        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(input.Email, "Testing1234!", "Confirmation Org", input.Cuit));
        result.IsSuccess.ShouldBeTrue();
        return input;
    }

    /// <summary>
    /// The authenticated branch, the only one that still mails a confirmation for a tenant and a membership that
    /// already exist and wait. A case about that lifecycle has to be born from the handler that owns it, because
    /// rows written by hand would let the assertion keep passing after the handler stopped producing them.
    /// </summary>
    private static async Task RegisterAsSignedInCallerAsync()
    {
        var email = $"confirm-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);
        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Confirmation Org", "30-12345678-1"));
        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// A conflicted finalization keeps answering that it conflicted.
    /// <para>
    /// Both terminal outcomes spend the envelope, so the envelope alone cannot tell them apart — the intent's
    /// recorded outcome is what a replay must read. Answering the success shortcut here would tell a person whose
    /// organization was lost that it had been created, and would do it every time they clicked the link again.
    /// </para>
    /// </summary>
    [Test]
    public async Task Replaying_a_conflicted_finalization_returns_the_conflict_it_recorded()
    {
        var registration = await RegisterAsync();
        var token = TestApp.GetRegistrationRawToken();

        // The CUIT is taken by somebody who proved their address first, which is the race the intent settles on.
        var winner = $"winner-{Guid.NewGuid():N}@example.test";
        var winnerId = await TestApp.RunAsUserAsync(winner, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(winnerId, winner);
        (await TestApp.SendAsync(new RegisterOrganizationCommand(winner, "Testing1234!", "Winning Org", registration.Cuit)))
            .IsSuccess.ShouldBeTrue();
        TestApp.SetUserId(null);
        TestApp.SetValidatedOptionalSession(null, null);

        var command = new ConfirmEmailCommand(token);
        var first = await TestApp.SendAsync(command);
        var replay = await TestApp.SendAsync(command);

        first.Error!.Code.ShouldBe("registration_conflict");
        replay.Error!.Code.ShouldBe("registration_conflict", "a replay returns the outcome the intent recorded, not the envelope's.");
        (await TestApp.ListAsync<PendingRegistrationIntent>())
            .Single(intent => intent.NormalizedEmail == registration.Email)
            .Outcome.ShouldBe(PendingRegistrationIntentOutcome.Conflicted);

        // The proof still earned the account, and neither the first answer nor the replay created a second one.
        (await TestApp.ListAsync<ApplicationUser>()).Count(user => user.Email == registration.Email).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1, "the winner's organization is the only one.");
    }

    /// <summary>
    /// The state a refused confirmation must leave: the intent still finalizable by the person who holds the token,
    /// its envelope still spendable exactly once, and none of the claims that only a spent proof may create.
    /// </summary>
    private static async Task AssertUnspentIntentAsync()
    {
        (await TestApp.ListAsync<PendingRegistrationIntent>()).Single().Outcome.ShouldBeNull();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Pending);
        await AssertNothingWasCreatedAsync();
    }

    private static async Task AssertNothingWasCreatedAsync()
    {
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<MembershipRole>()).ShouldBe(0);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "identity.confirmed").ShouldBe(0);
    }

    private sealed record RegistrationInput(string Email, string Cuit);
}
