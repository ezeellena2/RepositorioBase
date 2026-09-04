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
        sink.Sent.Single().Body.ShouldContain(KnownToken);
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

    private const string InvitationType = "identity.invitation.requested";
    private const string KnownToken = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=";

    private static OutboxDispatcher DispatcherFor(IServiceScope scope, TimeProvider clock, TestEmailSink sink)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new OutboxDispatcher(
            context,
            new OutboxSecretReader(context, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()),
            [new InvitationEmailDeliveryHandler(context, sink), new EmailConfirmationDeliveryHandler(context, sink)],
            clock);
    }

    /// <summary>
    /// A real invitation behind the message, because the handler resolves the recipient from it rather than from
    /// the payload — an address is PII and IA-REQ-029 keeps PII out of an outbox payload exactly as it keeps
    /// tokens out.
    /// </summary>
    private static async Task<OutboxMessage> SeedAsync(
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
