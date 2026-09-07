using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What a deployment that has not been admitted does with its outbox (IA-REQ-055).
/// <para>
/// The contract says a closed deployment "dispatches no outbox delivery", and the guard has to sit before the
/// <em>claim</em> rather than before the send. A dispatcher that claimed a message and then declined to deliver it
/// would take a lease and spend an attempt off a budget of eight — so a deployment that sat closed for long enough
/// would exhaust messages it never tried to send, and open to find them dead. That is the failure this file exists
/// to make impossible.
/// </para>
/// </summary>
public sealed class OutboxAdmissionTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly List<Guid> _messageIds = [];

    /// <summary>
    /// The messages and their envelopes, and nothing else. A delivered message writes an append-only audit row
    /// that a trigger refuses to delete, and the tenant behind it cannot be removed while that row names it — so
    /// the premise rows are left where the sibling delivery suite leaves its own.
    /// </summary>
    [TearDown]
    public async Task Remove_only_this_tests_messages()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboxSecrets.Where(secret => _messageIds.Contains(secret.OutboxMessageId)).ExecuteDeleteAsync();
        await context.OutboxMessages.Where(message => _messageIds.Contains(message.Id)).ExecuteDeleteAsync();
        _messageIds.Clear();
    }

    [TestCase(RecoveryAdmissionState.Closed)]
    [TestCase(RecoveryAdmissionState.Quarantined)]
    public async Task A_deployment_that_is_not_open_delivers_nothing(RecoveryAdmissionState state)
    {
        using var scope = TestServices.CreateScope();
        var sink = new TestEmailSink();
        await SeedDueMessageAsync(scope);

        var delivered = await DispatcherFor(scope, sink, Admission(state)).DispatchDueAsync(CancellationToken.None);

        delivered.ShouldBe(0);
        sink.Sent.ShouldBeEmpty($"a {state} deployment dispatches no outbox delivery");
    }

    /// <summary>
    /// The part that matters more than the refusal itself. A refused pass must leave the message exactly as it
    /// found it — unclaimed, unleased, and with every one of its eight attempts still available — because the
    /// deployment is going to open eventually and these messages have to survive until it does.
    /// </summary>
    [TestCase(RecoveryAdmissionState.Closed)]
    [TestCase(RecoveryAdmissionState.Quarantined)]
    public async Task A_refused_pass_spends_nothing_and_claims_nothing(RecoveryAdmissionState state)
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedDueMessageAsync(scope);

        for (var pass = 0; pass < 3; pass++)
        {
            await DispatcherFor(scope, new TestEmailSink(), Admission(state)).DispatchDueAsync(CancellationToken.None);
        }

        var after = await ReadAsync(message);
        after.Status.ShouldBe(OutboxMessageStatus.Pending);
        after.AttemptCount.ShouldBe(0, "a deployment that never tried has not used an attempt");
        after.LeaseOwner.ShouldBeNull("a refused pass claims nothing");
        after.LeaseExpiresAt.ShouldBeNull();
        after.FirstAttemptAt.ShouldBeNull("nothing was attempted, so nothing started the receipt window");
    }

    /// <summary>The messages a closed deployment held are still deliverable when it opens.</summary>
    [Test]
    public async Task What_a_closed_deployment_held_is_delivered_once_it_opens()
    {
        using var scope = TestServices.CreateScope();
        var message = await SeedDueMessageAsync(scope);
        await DispatcherFor(scope, new TestEmailSink(), Admission(RecoveryAdmissionState.Closed)).DispatchDueAsync(CancellationToken.None);

        var sink = new TestEmailSink();
        var delivered = await DispatcherFor(scope, sink, Admission(RecoveryAdmissionState.Open)).DispatchDueAsync(CancellationToken.None);

        delivered.ShouldBe(1);
        sink.Sent.ShouldNotBeEmpty();
        (await ReadAsync(message)).Status.ShouldBe(OutboxMessageStatus.Delivered);
    }

    /// <summary>A deployment nobody armed is not recovering, and its outbox is untouched by any of this.</summary>
    [Test]
    public async Task A_deployment_nobody_armed_delivers_exactly_as_it_always_did()
    {
        using var scope = TestServices.CreateScope();
        var sink = new TestEmailSink();
        await SeedDueMessageAsync(scope);

        var delivered = await DispatcherFor(scope, sink, new FixedAdmission(RecoveryAdmission.NotRecovering)).DispatchDueAsync(CancellationToken.None);

        delivered.ShouldBe(1);
        sink.Sent.ShouldNotBeEmpty();
    }

    private static IRecoveryAdmission Admission(RecoveryAdmissionState state) => new FixedAdmission(
        new RecoveryAdmission(state, RecoveryAdmissionReason.Verified, Origin));

    private static OutboxDispatcher DispatcherFor(IServiceScope scope, TestEmailSink sink, IRecoveryAdmission admission)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new OutboxDispatcher(
            context,
            new OutboxSecretReader(context, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()),
            [new InvitationEmailDeliveryHandler(context, EmailOptions)],
            new ControlledTimeProvider(Origin),
            sink,
            admission);
    }

    /// <summary>
    /// A message that is due now and would certainly be delivered, so a pass that delivers nothing did so because
    /// of admission and not because there was nothing to do. It is a real invitation behind a real sealed
    /// envelope, because the handler resolves the recipient from the invitation rather than from the payload.
    /// </summary>
    private async Task<Guid> SeedDueMessageAsync(IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxSecretWriter>();
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var tenant = Tenant.CreateOrganization(TenantSlug.From($"admission-{Guid.NewGuid():N}"));
        tenant.Activate();
        var role = Role.Create(tenant, $"member-{Guid.NewGuid():N}");
        var invitation = Invitation.Issue(
            tenant,
            $"invitee-{Guid.NewGuid():N}@example.test",
            [role],
            VersionedTokenHash.Of($"{rawToken}|{Guid.NewGuid():N}"),
            Origin.AddMinutes(-2),
            Origin.AddDays(7));
        context.AddRange(tenant, role, invitation);

        var message = OutboxMessage.Create(
            "identity.invitation.requested",
            System.Text.Json.JsonSerializer.Serialize(new { InvitationId = invitation.Id.Value, TenantId = tenant.Id.Value }),
            Origin.AddMinutes(-1));
        context.OutboxMessages.Add(message);
        context.OutboxSecrets.Add(OutboxSecret.Create(
            message.Id,
            VersionedTokenHash.Of($"{rawToken}|{message.Id}").Value,
            writer.Encrypt(rawToken),
            Origin.AddHours(24)));
        await context.SaveChangesAsync();
        _messageIds.Add(message.Id);
        return message.Id;
    }

    private static async Task<OutboxMessage> ReadAsync(Guid messageId)
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
    }

    private static readonly Microsoft.Extensions.Options.IOptions<CleanArchitecture.Infrastructure.Email.IdentityEmailOptions> EmailOptions =
        Microsoft.Extensions.Options.Options.Create(
            new CleanArchitecture.Infrastructure.Email.IdentityEmailOptions { PublicOrigin = "https://app.example.test" });

    private sealed class FixedAdmission(RecoveryAdmission current) : IRecoveryAdmission
    {
        public RecoveryAdmission Current { get; } = current;
    }
}
