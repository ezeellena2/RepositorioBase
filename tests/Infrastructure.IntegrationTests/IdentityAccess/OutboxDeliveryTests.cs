using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using System.Text.Json;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// The delivery loop against real PostgreSQL and a clock the test moves by hand. Everything timed here is
/// asserted rather than waited for, which is the only way a backoff schedule measured in minutes is testable at
/// all (IA-REQ-018/028).
/// </summary>
public sealed class OutboxDeliveryTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly List<Guid> _messageIds = [];
    private readonly List<Guid> _identityIds = [];
    private readonly List<Guid> _resetIds = [];

    [TearDown]
    public async Task Remove_only_this_tests_messages()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboxSecrets.Where(secret => _messageIds.Contains(secret.OutboxMessageId)).ExecuteDeleteAsync();
        await context.OutboxMessages.Where(message => _messageIds.Contains(message.Id)).ExecuteDeleteAsync();
        await context.PasswordResetRequests.Where(request => _resetIds.Contains(request.Id)).ExecuteDeleteAsync();
        await context.Users.Where(user => _identityIds.Contains(user.Id)).ExecuteDeleteAsync();
        _messageIds.Clear();
        _resetIds.Clear();
        _identityIds.Clear();
    }

    /// <summary>A message due later is not work yet; claiming it early would deliver a retry before its backoff.</summary>
    [Test]
    public async Task A_message_is_claimed_only_once_it_is_due()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(scope, InvitationType, Origin.AddMinutes(5));

        (await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None)).ShouldBe(0);
        sink.Sent.ShouldBeEmpty();

        clock.Advance(TimeSpan.FromMinutes(5));
        (await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None)).ShouldBe(1);
        sink.Sent.Count.ShouldBe(1);
        (await ReloadAsync(scope, message.Id)).Status.ShouldBe(OutboxMessageStatus.Delivered);
    }

    /// <summary>
    /// The message id is the idempotency key, so a second pass over an already delivered message sends nothing.
    /// Without this a worker restart between the send and the local update delivers twice.
    /// </summary>
    [Test]
    public async Task A_delivered_message_is_never_sent_again()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(scope, InvitationType, Origin);

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        sink.Sent.Count.ShouldBe(1);
        sink.Sent[0].IdempotencyKey.ShouldBe(message.Id.ToString(), "the outbox message id is the send idempotency key");
    }

    /// <summary>
    /// Success is one local transaction: the message becomes terminal and the envelope stops holding a token,
    /// while every piece of non-secret evidence survives for whoever has to answer for the delivery later.
    /// </summary>
    [Test]
    public async Task A_delivered_message_clears_its_ciphertext_and_keeps_its_evidence()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(scope, InvitationType, Origin);

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        var delivered = await ReloadAsync(scope, message.Id);
        delivered.Status.ShouldBe(OutboxMessageStatus.Delivered);
        delivered.DeliveredAt.ShouldBe(Origin);
        delivered.LeaseOwner.ShouldBeNull("a terminal message holds no lease");

        var secret = await SecretOfAsync(scope, message.Id);
        secret.Ciphertext.ShouldBeNull("the envelope stops holding the token the moment it is delivered");
        secret.Status.ShouldBe(OutboxSecretStatus.Delivered);
        secret.ProviderReceipt.ShouldNotBeNullOrWhiteSpace("the receipt is what a retry reconciles against");
        secret.ExpiresAt.ShouldNotBe(default, "the window is evidence too and is not erased with the ciphertext");
        // Delivered is not completed: the recipient still has to use the token, and CompletedAt belongs to
        // the terminalization that follows. What delivery leaves behind is when it happened and by whom.
        secret.DeliveredAt.ShouldBe(Origin);
        secret.DeliveryReason.ShouldNotBeNullOrWhiteSpace();
        secret.CompletedAt.ShouldBeNull("a delivered token is still usable, so its envelope is not terminal yet");
    }

    /// <summary>
    /// A transient failure costs one attempt and schedules the next: 30s, 60s, 120s... capped at 30 minutes. The
    /// lease is released so another worker may take it when it comes due, and the failure code is an allowlisted
    /// token rather than whatever the provider said.
    /// </summary>
    [TestCase(1, 30)]
    [TestCase(2, 60)]
    [TestCase(3, 120)]
    [TestCase(7, 1800)]
    public async Task A_transient_failure_schedules_the_next_attempt_with_capped_exponential_backoff(int attempt, int expectedSeconds)
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, false) };
        var message = await SeedAsync(scope, InvitationType, Origin);

        for (var pass = 0; pass < attempt; pass++)
        {
            await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
            var pending = await ReloadAsync(scope, message.Id);
            if (pass < attempt - 1) clock.Advance(pending.NextAttemptAt - clock.GetUtcNow());
        }

        var retried = await ReloadAsync(scope, message.Id);
        retried.AttemptCount.ShouldBe(attempt);
        retried.Status.ShouldBe(OutboxMessageStatus.Pending, "a transient failure is not terminal");
        retried.LeaseOwner.ShouldBeNull("a released lease lets another worker take the retry");
        retried.LeaseExpiresAt.ShouldBeNull();
        (retried.NextAttemptAt - clock.GetUtcNow()).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    /// <summary>Eight attempts is the end of it; a message nothing will retry must be distinguishable from one still due.</summary>
    [Test]
    public async Task Eight_exhausted_attempts_become_permanent()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, false) };
        var message = await SeedAsync(scope, InvitationType, Origin);

        for (var pass = 0; pass < 8; pass++)
        {
            await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromHours(1));
        }

        var abandoned = await ReloadAsync(scope, message.Id);
        abandoned.Status.ShouldBe(OutboxMessageStatus.Abandoned);
        abandoned.AttemptCount.ShouldBe(8);
        abandoned.FailureCode.ShouldNotBeNullOrWhiteSpace();
        (await SecretOfAsync(scope, message.Id)).Ciphertext.ShouldBeNull("a message nothing will send again holds no token");

        var afterwards = sink.Sent.Count;
        clock.Advance(TimeSpan.FromDays(1));
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        sink.Sent.Count.ShouldBe(afterwards, "an abandoned message is never claimed again");
    }

    /// <summary>An envelope past its window is terminalized without sending, and without exposing the token.</summary>
    [Test]
    public async Task An_expired_envelope_is_terminalized_without_sending()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(scope, InvitationType, Origin, envelopeExpiresAt: Origin.AddMinutes(-1));

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        sink.Sent.ShouldBeEmpty("an expired token is not delivered, however due the message is");
        (await ReloadAsync(scope, message.Id)).Status.ShouldBe(OutboxMessageStatus.Abandoned);
        var secret = await SecretOfAsync(scope, message.Id);
        secret.Status.ShouldBe(OutboxSecretStatus.Expired);
        secret.Ciphertext.ShouldBeNull();
    }

    /// <summary>A provider that will never accept this message is not worth seven more attempts.</summary>
    [Test]
    public async Task A_permanent_provider_failure_stops_immediately()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, true) };
        var message = await SeedAsync(scope, InvitationType, Origin);

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        var abandoned = await ReloadAsync(scope, message.Id);
        abandoned.Status.ShouldBe(OutboxMessageStatus.Abandoned);
        abandoned.AttemptCount.ShouldBe(1, "a permanent refusal costs one attempt, not the whole budget");
        (await SecretOfAsync(scope, message.Id)).Ciphertext.ShouldBeNull();
    }

    /// <summary>
    /// The provider's exception text is the most likely place for a token, an address or a stack to end up in a
    /// column. Only an allowlisted code is kept (IA-REQ-029).
    /// </summary>
    [Test]
    public async Task A_provider_exception_never_reaches_a_column()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink { Throw = new InvalidOperationException("smtp said: token=SUPER-SECRET user=ana@example.test") };
        var message = await SeedAsync(scope, InvitationType, Origin);

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        var failed = await ReloadAsync(scope, message.Id);
        failed.AttemptCount.ShouldBe(1);
        failed.FailureCode.ShouldNotBeNull();
        failed.FailureCode!.ShouldNotContain("SUPER-SECRET");
        failed.FailureCode.ShouldNotContain("ana@example.test");
        failed.FailureCode.ShouldNotContain("smtp");
    }

    /// <summary>The raw token reaches the provider and nothing else: not the payload, not a column, not a log.</summary>
    [Test]
    public async Task The_raw_token_reaches_the_provider_and_no_column()
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(scope, InvitationType, Origin, rawToken: KnownToken);

        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);

        // The recipient needs the token; that is the whole point of delivering it.
        Uri.UnescapeDataString(sink.Sent.Single().Body).ShouldContain(KnownToken);
        var delivered = await ReloadAsync(scope, message.Id);
        delivered.Payload.ShouldNotContain(KnownToken);
        (delivered.FailureCode ?? string.Empty).ShouldNotContain(KnownToken);
        var secret = await SecretOfAsync(scope, message.Id);
        (secret.Ciphertext ?? string.Empty).ShouldNotContain(KnownToken);
        secret.VersionedHash.ShouldNotContain(KnownToken);
        (secret.ProviderReceipt ?? string.Empty).ShouldNotContain(KnownToken);
    }

    /// <summary>
    /// Two workers over one due message. The claim is a compare-and-swap over the generation, so only one of them
    /// may hold it — a lease granted twice is a message delivered twice.
    /// </summary>
    [Test]
    public async Task Two_workers_over_one_due_message_deliver_it_once()
    {
        using var first = TestServices.CreateScope();
        using var second = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var message = await SeedAsync(first, InvitationType, Origin);
        using var barrier = new Barrier(2);

        var claimed = await Task.WhenAll(
            Task.Run(async () => { barrier.SignalAndWait(TimeSpan.FromSeconds(30)); return await DispatcherFor(first, clock, sink).DispatchDueAsync(CancellationToken.None); }),
            Task.Run(async () => { barrier.SignalAndWait(TimeSpan.FromSeconds(30)); return await DispatcherFor(second, clock, sink).DispatchDueAsync(CancellationToken.None); }));

        claimed.Sum().ShouldBe(1, "exactly one worker may claim a due message");
        sink.Sent.Count.ShouldBe(1);
        (await ReloadAsync(first, message.Id)).Status.ShouldBe(OutboxMessageStatus.Delivered);
    }

    [TestCase("provider_error")]
    [TestCase("handler_missing")]
    [TestCase("envelope_unreadable")]
    public async Task Every_exhausted_failure_clears_the_secret(string failure)
    {
        using var scope = TestServices.CreateScope();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        if (failure == "provider_error") sink.Throw = new InvalidOperationException("private-provider-text");
        var message = await SeedAsync(scope, failure == "handler_missing" ? "unhandled" : InvitationType, Origin);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (failure == "envelope_unreadable")
            await context.OutboxSecrets.Where(secret => secret.OutboxMessageId == message.Id).ExecuteUpdateAsync(setters => setters.SetProperty(secret => secret.Ciphertext, "unreadable"));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromHours(1));
        }
        var abandoned = await ReloadAsync(scope, message.Id);
        abandoned.Status.ShouldBe(OutboxMessageStatus.Abandoned);
        abandoned.AttemptCount.ShouldBe(8);
        abandoned.LeaseOwner.ShouldBeNull();
        var terminal = await SecretOfAsync(scope, message.Id);
        terminal.Status.ShouldBe(OutboxSecretStatus.Failed);
        terminal.Ciphertext.ShouldBeNull();
        terminal.CompletedAt.ShouldNotBeNull();
    }

    private const string InvitationType = "identity.invitation.requested";

    [Test]
    public async Task Eight_crashed_attempts_cannot_trigger_a_ninth_provider_request()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboxMessages.Where(item => item.Id == message.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.AttemptCount, 8).SetProperty(item => item.Generation, 8)
            .SetProperty(item => item.FirstAttemptAt, Origin).SetProperty(item => item.LeaseOwner, "crashed-worker")
            .SetProperty(item => item.LeaseExpiresAt, Origin));
        var sink = new TestEmailSink();
        await DispatcherFor(scope, new ControlledTimeProvider(Origin), sink).DispatchDueAsync(CancellationToken.None);
        sink.Sent.ShouldBeEmpty();
        var abandoned = await ReloadAsync(scope, message.Id);
        abandoned.AttemptCount.ShouldBe(8);
        abandoned.Status.ShouldBe(OutboxMessageStatus.Abandoned);
        abandoned.FailureCode.ShouldBe("attempts_exhausted");
        (await SecretOfAsync(scope, message.Id)).Ciphertext.ShouldBeNull();
    }

    [Test]
    public async Task Cleanup_preserves_an_already_consumed_secrets_audit_evidence()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secret = await context.OutboxSecrets.SingleAsync(item => item.OutboxMessageId == message.Id);
        secret.Consume("confirmation_consumed", Origin);
        await context.SaveChangesAsync();
        var sink = new TestEmailSink();
        await DispatcherFor(scope, new ControlledTimeProvider(Origin.AddMinutes(1)), sink).DispatchDueAsync(CancellationToken.None);
        var retained = await SecretOfAsync(scope, message.Id);
        retained.Status.ShouldBe(OutboxSecretStatus.Consumed);
        retained.TerminalReason.ShouldBe("confirmation_consumed");
        retained.CompletedAt.ShouldBe(Origin);
        sink.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task A_required_missing_secret_exhausts_without_fabricating_an_envelope()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboxSecrets.Where(secret => secret.OutboxMessageId == message.Id).ExecuteDeleteAsync();
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromHours(1));
        }
        var abandoned = await ReloadAsync(scope, message.Id);
        abandoned.Status.ShouldBe(OutboxMessageStatus.Abandoned);
        abandoned.AttemptCount.ShouldBe(8);
        abandoned.FailureCode.ShouldBe("secret_missing");
        abandoned.LeaseOwner.ShouldBeNull();
        (await context.OutboxSecrets.AnyAsync(secret => secret.OutboxMessageId == message.Id)).ShouldBeFalse();
        sink.Sent.ShouldBeEmpty();
    }

    [TestCase(6, true)]
    [TestCase(1440, false)]
    public Task Acknowledged_send_then_local_rollback_reconciles_only_within_Resends_retention(int minutes, bool canReconcile) =>
        AssertAcknowledgedRollbackAsync(minutes, canReconcile, rotateKey: false);

    [Test]
    public Task Changed_api_key_never_replays_an_uncertain_request() => AssertAcknowledgedRollbackAsync(6, canReconcile: false, rotateKey: true);

    private async Task AssertAcknowledgedRollbackAsync(int minutes, bool canReconcile, bool rotateKey)
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin, Origin.AddDays(3));
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var clock = new ControlledTimeProvider(Origin);
        var transport = new IdempotentResendTransport();
        using var client = new HttpClient(transport);
        var emailOptions = Microsoft.Extensions.Options.Options.Create(
            new CleanArchitecture.Infrastructure.Email.IdentityEmailOptions { ApiKey = "isolated-test-key", FromAddress = "sender@example.test", PublicOrigin = "https://app.example.test" });
        var sender = new CleanArchitecture.Infrastructure.Email.IdentityEmailAdapter(emailOptions, client);
        var failingOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(context.Database.GetConnectionString())
            .AddInterceptors(new RejectDeliveredSecretSave()).Options;
        await using (var failing = new ApplicationDbContext(failingOptions))
        {
            var first = new OutboxDispatcher(failing, new OutboxSecretReader(failing, protector),
                [new InvitationEmailDeliveryHandler(failing, EmailOptions)], clock, sender, NotRecovering, TestMetrics.Instance);
            await Should.ThrowAsync<InvalidOperationException>(() => first.DispatchDueAsync(CancellationToken.None));
        }
        transport.AcceptedCount.ShouldBe(1);
        var pending = await ReloadAsync(scope, message.Id);
        pending.Status.ShouldBe(OutboxMessageStatus.Pending);
        pending.FirstAttemptAt.ShouldBe(Origin);
        pending.AttemptCount.ShouldBe(1);
        pending.RequestFingerprint.ShouldNotBeNullOrWhiteSpace();
        (await SecretOfAsync(scope, message.Id)).Ciphertext.ShouldNotBeNull();

        clock.Advance(TimeSpan.FromMinutes(minutes));
        if (rotateKey) emailOptions.Value.ApiKey = "rotated-isolated-test-key";
        var retry = new OutboxDispatcher(context, new OutboxSecretReader(context, protector),
            [new InvitationEmailDeliveryHandler(context, EmailOptions)], clock, sender, NotRecovering, TestMetrics.Instance);
        await retry.DispatchDueAsync(CancellationToken.None);
        await retry.DispatchDueAsync(CancellationToken.None);
        transport.AcceptedCount.ShouldBe(1, "the transport accepts one logical message even after a process restart");
        transport.RequestCount.ShouldBe(canReconcile ? 2 : 1);
        var settled = await ReloadAsync(scope, message.Id);
        settled.Status.ShouldBe(canReconcile ? OutboxMessageStatus.Delivered : OutboxMessageStatus.Abandoned);
        var secret = await SecretOfAsync(scope, message.Id);
        secret.Ciphertext.ShouldBeNull();
        if (canReconcile) secret.ProviderReceipt.ShouldBe(transport.Receipt);
        else settled.FailureCode.ShouldBe(rotateKey ? "request_changed" : "receipt_window_expired");
    }

    private sealed class RejectDeliveredSecretSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxSecret>().Any(entry => entry.Entity.Status == OutboxSecretStatus.Delivered))
                throw new InvalidOperationException("isolated settlement failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class IdempotentResendTransport : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _accepted = [];
        public string Receipt { get; } = Guid.NewGuid().ToString();
        public int AcceptedCount => _accepted.Count;
        public int RequestCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var key = request.Headers.GetValues("Idempotency-Key").Single();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (_accepted.TryGetValue(key, out var existing)) body.ShouldBe(existing, "receipt reconciliation must repeat the exact payload");
            else _accepted.Add(key, body);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id = Receipt })) };
        }
    }

    [Test]
    public async Task A_stale_worker_cannot_settle_a_message_reclaimed_by_another_worker()
    {
        using var first = TestServices.CreateScope();
        using var second = TestServices.CreateScope();
        var message = await SeedAsync(first, InvitationType, Origin);
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink();
        var reclaimedCount = 0;
        sink.BeforeSend = async () =>
        {
            sink.BeforeSend = null;
            clock.Advance(TimeSpan.FromMinutes(6));
            reclaimedCount = await DispatcherFor(second, clock, sink).DispatchDueAsync(CancellationToken.None);
        };
        (await DispatcherFor(first, clock, sink).DispatchDueAsync(CancellationToken.None)).ShouldBe(0);
        reclaimedCount.ShouldBe(1);
        sink.AcceptedCount.ShouldBe(1);
        (await ReloadAsync(first, message.Id)).Generation.ShouldBe(2);
        (await SecretOfAsync(first, message.Id)).Status.ShouldBe(OutboxSecretStatus.Delivered);
    }

    [Test]
    public async Task A_slow_earlier_send_does_not_deliver_a_later_expired_envelope()
    {
        using var scope = TestServices.CreateScope();
        await SeedAsync(scope, InvitationType, Origin.AddSeconds(-2));
        var later = await SeedAsync(scope, InvitationType, Origin.AddSeconds(-1), Origin.AddMinutes(1));
        var clock = new ControlledTimeProvider(Origin);
        var sink = new TestEmailSink { BeforeSend = () => { clock.Advance(TimeSpan.FromMinutes(2)); return Task.CompletedTask; } };
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        sink.Sent.Count.ShouldBe(1);
        (await SecretOfAsync(scope, later.Id)).Status.ShouldBe(OutboxSecretStatus.Expired);
    }

    [Test]
    public async Task A_claim_persists_the_first_attempt_time_before_external_delivery()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin);
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, false) };
        await DispatcherFor(scope, new ControlledTimeProvider(Origin), sink).DispatchDueAsync(CancellationToken.None);
        (await ReloadAsync(scope, message.Id)).FirstAttemptAt.ShouldBe(Origin);
        (await ReloadAsync(scope, message.Id)).RequestFingerprint.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task An_ambiguous_attempt_is_not_sent_again_after_the_provider_retention_window()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin, Origin.AddDays(3));
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, false) };
        var clock = new ControlledTimeProvider(Origin);
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(24));
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        sink.Sent.Count.ShouldBe(1);
        (await ReloadAsync(scope, message.Id)).Status.ShouldBe(OutboxMessageStatus.Abandoned);
        (await SecretOfAsync(scope, message.Id)).Ciphertext.ShouldBeNull();
    }

    [Test]
    public async Task Changed_recipient_is_not_sent_using_an_existing_idempotency_key()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedAsync(scope, InvitationType, Origin);
        var sink = new TestEmailSink { Respond = _ => new EmailDeliveryReceipt(false, null, false) };
        var clock = new ControlledTimeProvider(Origin);
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"Invitations\" SET \"NormalizedEmail\" = 'changed@example.test' WHERE \"Id\" = {0}", JsonDocument.Parse(message.Payload).RootElement.GetProperty("InvitationId").GetGuid());
        clock.Advance(TimeSpan.FromMinutes(1));
        await DispatcherFor(scope, clock, sink).DispatchDueAsync(CancellationToken.None);
        sink.Sent.Count.ShouldBe(1);
        (await ReloadAsync(scope, message.Id)).FailureCode.ShouldBe("request_changed");
    }
    private const string KnownToken = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=";

    private static OutboxDispatcher DispatcherFor(IServiceScope scope, TimeProvider clock, TestEmailSink sink)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new OutboxDispatcher(
            context,
            new OutboxSecretReader(context, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()),
            [new InvitationEmailDeliveryHandler(context, EmailOptions), new EmailConfirmationDeliveryHandler(context, EmailOptions)],
            clock, sink, NotRecovering, TestMetrics.Instance);
    }

    /// <summary>
    /// The reset link a person actually receives (IA-REQ-051). The recipient is resolved from the identity the
    /// request belongs to, the token reaches the mail only in the fragment, and the stored payload carries neither.
    /// </summary>
    [Test]
    public async Task A_password_recovery_message_carries_its_link_and_leaves_the_token_out_of_the_payload()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxSecretWriter>();
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var sink = new TestEmailSink();

        var identityId = Guid.NewGuid();
        var email = $"forgetful-{Guid.NewGuid():N}@example.test";
        context.Users.Add(new CleanArchitecture.Infrastructure.Identity.ApplicationUser
        {
            Id = identityId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        _identityIds.Add(identityId);

        const string rawToken = "cGFzc3dvcmQtcmVjb3ZlcnktdG9rZW4tZm9yLW9uZS10ZXN0MQ==";
        var reset = CleanArchitecture.Domain.IdentityAccess.Credentials.PasswordResetRequest.Issue(
            identityId, VersionedTokenHash.Of(rawToken), clock.GetUtcNow(), TimeSpan.FromMinutes(30));
        context.PasswordResetRequests.Add(reset);
        _resetIds.Add(reset.Id);

        var message = OutboxMessage.Create(
            "identity.password.recovery.requested",
            JsonSerializer.Serialize(new { RequestId = reset.Id }),
            clock.GetUtcNow());
        _messageIds.Add(message.Id);
        context.OutboxMessages.Add(message);
        context.OutboxSecrets.Add(OutboxSecret.Create(
            message.Id,
            VersionedTokenHash.Of($"{rawToken}|{message.Id}").Value,
            writer.Encrypt(rawToken),
            clock.GetUtcNow().AddMinutes(30)));
        await context.SaveChangesAsync();

        var dispatcher = new OutboxDispatcher(
            context,
            new OutboxSecretReader(context, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()),
            [new PasswordRecoveryDeliveryHandler(context, EmailOptions)],
            clock,
            sink,
            NotRecovering, TestMetrics.Instance);
        (await dispatcher.DispatchDueAsync(CancellationToken.None)).ShouldBe(1);

        var sent = sink.Sent.Single();
        sent.Recipient.ShouldBe(email);
        sent.Subject.ShouldBe("Reset your password");
        sent.Body.ShouldContain($"https://app.example.test/credentials/reset#token={Uri.EscapeDataString(rawToken)}");
        message.Payload.ShouldNotContain(rawToken);
        message.Payload.ShouldNotContain(email);
    }

    /// <summary>
    /// A deployment nobody armed. These tests are about what the dispatcher does with a message, not about
    /// whether the deployment may dispatch at all — that question has its own file (IA-REQ-055).
    /// </summary>
    private static readonly CleanArchitecture.Application.IdentityAccess.Lifecycle.IRecoveryAdmission NotRecovering = new OpenAdmission();

    private sealed class OpenAdmission : CleanArchitecture.Application.IdentityAccess.Lifecycle.IRecoveryAdmission
    {
        public CleanArchitecture.Application.IdentityAccess.Lifecycle.RecoveryAdmission Current { get; } =
            CleanArchitecture.Application.IdentityAccess.Lifecycle.RecoveryAdmission.NotRecovering;
    }

    private static readonly Microsoft.Extensions.Options.IOptions<CleanArchitecture.Infrastructure.Email.IdentityEmailOptions> EmailOptions =
        Microsoft.Extensions.Options.Options.Create(new CleanArchitecture.Infrastructure.Email.IdentityEmailOptions { PublicOrigin = "https://app.example.test" });

    /// <summary>
    /// A real invitation behind the message, because the handler resolves the recipient from it rather than from
    /// the payload — an address is PII and IA-REQ-029 keeps PII out of an outbox payload exactly as it keeps
    /// tokens out.
    /// </summary>
    private async Task<OutboxMessage> SeedAsync(
        IServiceScope scope,
        string type,
        DateTimeOffset dueAt,
        DateTimeOffset? envelopeExpiresAt = null,
        string rawToken = KnownToken)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxSecretWriter>();

        var tenant = Tenant.CreateOrganization(TenantSlug.From($"outbox-{Guid.NewGuid():N}"));
        tenant.Activate();
        var role = Role.Create(tenant, $"member-{Guid.NewGuid():N}");
        var invitation = Invitation.Issue(
            tenant,
            $"invitee-{Guid.NewGuid():N}@example.test",
            [role],
            VersionedTokenHash.Of($"{rawToken}|{Guid.NewGuid():N}"),
            dueAt.AddMinutes(-1),
            dueAt.AddDays(7));
        context.AddRange(tenant, role, invitation);

        var message = OutboxMessage.Create(type, JsonSerializer.Serialize(new { InvitationId = invitation.Id.Value, TenantId = tenant.Id.Value }), dueAt);
        _messageIds.Add(message.Id);
        context.OutboxMessages.Add(message);
        context.OutboxSecrets.Add(OutboxSecret.Create(
            message.Id,
            VersionedTokenHash.Of($"{rawToken}|{message.Id}").Value,
            writer.Encrypt(rawToken),
            envelopeExpiresAt ?? dueAt.AddHours(24)));
        await context.SaveChangesAsync();
        return message;
    }

    private static async Task<OutboxMessage> ReloadAsync(IServiceScope scope, Guid id)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id);
    }

    private static async Task<OutboxSecret> SecretOfAsync(IServiceScope scope, Guid messageId)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboxSecrets.AsNoTracking().SingleAsync(secret => secret.OutboxMessageId == messageId);
    }
}
