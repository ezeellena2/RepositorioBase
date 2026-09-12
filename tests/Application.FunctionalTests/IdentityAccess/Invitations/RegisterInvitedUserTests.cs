using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using CleanArchitecture.Infrastructure.IdentityAccess;
using CleanArchitecture.Infrastructure.Outbox;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// The public half of IA-REQ-016. This request only ever gets an identity as far as being confirmable: it never
/// accepts the invitation, and confirmation plus sign-in plus an authenticated acceptance still stand between it
/// and a membership.
/// <para>
/// Every outcome here is measured twice — once against a real invitation and once against a token that resolves
/// to nothing — because SPEC §6 makes the whole flow neutral: it must not reveal whether the token is real, and
/// within a real token it must not reveal whether the address already has an account.
/// </para>
/// </summary>
public sealed class RegisterInvitedUserTests : TestBase
{
    private const string ValidPassword = "Testing1234!";
    private const string PolicyViolatingPassword = "short";

    [Test]
    public async Task Registering_from_an_invitation_creates_the_confirmable_identity_and_never_a_membership()
    {
        TestApp.SetRequestLanguage("es");
        var (email, token) = await IssuedInvitationAsync();
        (await InvitationScenario.SingleInvitationAsync()).Language.ShouldBe("es");
        ResetToAnonymous();

        var result = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword));

        result.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        identity.EmailConfirmed.ShouldBeFalse("registration issues confirmation; it does not grant it");
        identity.PreferredLanguage.ShouldBe("es", "first account creation inherits the invitation snapshot");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "only the inviter's membership exists — registration creates none");
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending, "registration never accepts the invitation");
    }

    /// <summary>
    /// IA-REQ-016's existing-identity branch: credential input is ignored and the caller gets the same neutral
    /// outcome. Asserting the stored password hash is unchanged is what proves "ignored" rather than "overwritten".
    /// </summary>
    [Test]
    public async Task Registering_when_the_identity_already_exists_ignores_the_password_and_leaves_the_credential_alone()
    {
        TestApp.SetRequestLanguage("es");
        var (email, token) = await IssuedInvitationAsync();
        (await InvitationScenario.SingleInvitationAsync()).Language.ShouldBe("es");
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, ValidPassword, "en");
        var before = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var storedHash = before.PasswordHash;
        var identityCount = await TestApp.CountAsync<ApplicationUser>();
        ResetToAnonymous();

        var result = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, "AnotherPassword1!"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identityCount, "an existing address is never registered twice");
        var after = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        after.PasswordHash.ShouldBe(storedHash, "submitted credentials must not overwrite an account the caller may not own");
        after.PreferredLanguage.ShouldBe("en", "the neutral existing-account branch cannot overwrite a preference");
        after.EmailConfirmed.ShouldBeTrue("an already-confirmed identity is not un-confirmed by an invitation");
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
    }

    /// <summary>
    /// A token that resolves to nothing must look exactly like one that does. If it did not, anyone could probe
    /// the endpoint to learn which tokens are live.
    /// </summary>
    [Test]
    public async Task An_unknown_token_is_answered_exactly_like_a_valid_one_and_creates_nothing()
    {
        await IssuedInvitationAsync();
        var identityCount = await TestApp.CountAsync<ApplicationUser>();
        ResetToAnonymous();

        var result = await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(7), ValidPassword));

        result.IsSuccess.ShouldBeTrue("an unknown token is neutral, not a 404 and not a 400");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identityCount);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
    }

    [Test]
    public async Task A_lapsed_and_a_withdrawn_invitation_are_answered_exactly_like_an_unknown_token()
    {
        var (_, token) = await IssuedInvitationAsync();
        await InvitationTestState.ExpireAsync();
        ResetToAnonymous();
        var identityCount = await TestApp.CountAsync<ApplicationUser>();

        var lapsed = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword));

        lapsed.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identityCount, "a lapsed invitation creates no identity");

        await InvitationTestState.CancelAsync();
        var withdrawn = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword));

        withdrawn.IsSuccess.ShouldBeTrue("lifecycle state is not disclosed to a token holder");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identityCount);
    }

    /// <summary>
    /// The oracle this flow must not have. Password policy is a property of the submitted password alone, so it is
    /// decided before any state is read — which is why the answer is the same for a live token and a dead one, and
    /// why the token is never hashed at all when the password is refused.
    /// </summary>
    [Test]
    public async Task A_password_that_violates_the_policy_is_refused_identically_for_a_live_and_a_dead_token()
    {
        var (_, token) = await IssuedInvitationAsync();
        ResetToAnonymous();
        TestApp.ResetConfirmationTokenHashInvocationCount();
        var identitiesBefore = await TestApp.CountAsync<ApplicationUser>();
        var messagesBefore = await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxMessage>();
        var secretsBefore = await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxSecret>();

        var live = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, PolicyViolatingPassword));
        var dead = await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(9), PolicyViolatingPassword));

        live.IsFailure.ShouldBeTrue();
        live.Error!.Code.ShouldBe("validation_failed");
        live.Error.ValidationErrors.Keys.ShouldBe(["password"]);
        live.Error.ValidationErrors["password"].ShouldBe([
            "Passwords must be at least 12 characters.",
            "Passwords must have at least one non alphanumeric character.",
            "Passwords must have at least one digit ('0'-'9').",
            "Passwords must have at least one uppercase ('A'-'Z')."
        ]);
        string.Join(' ', live.Error.ValidationErrors["password"]).ShouldNotContain(PolicyViolatingPassword);
        dead.IsFailure.ShouldBeTrue();
        dead.Error!.Code.ShouldBe(live.Error.Code, "a weak password must not double as a token-validity oracle");
        dead.Error.Category.ShouldBe(live.Error.Category);
        dead.Error.ValidationErrors["password"].ShouldBe(live.Error.ValidationErrors["password"]);
        TestApp.ConfirmationTokenHashInvocationCount.ShouldBe(0, "a refused password is decided before the token is read at all");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identitiesBefore);
        (await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).ShouldBe(messagesBefore);
        (await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxSecret>()).ShouldBe(secretsBefore);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
    }

    /// <summary>
    /// Same rule seen from the other axis: with a live token, a policy-violating password is refused whether or
    /// not the address already has an account. That is what stops the refusal from revealing existence.
    /// </summary>
    [Test]
    public async Task A_policy_violating_password_is_refused_whether_or_not_the_address_already_has_an_account()
    {
        var (email, token) = await IssuedInvitationAsync();
        ResetToAnonymous();

        var absent = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, PolicyViolatingPassword));

        await InvitationScenario.SeedConfirmedRecipientAsync(email);
        var present = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, PolicyViolatingPassword));

        absent.IsFailure.ShouldBeTrue();
        present.IsFailure.ShouldBeTrue();
        present.Error!.Code.ShouldBe(absent.Error!.Code);
        present.Error.Category.ShouldBe(absent.Error.Category);
    }

    /// <summary>
    /// Registration is delivery-bearing: the invitee has to be told to confirm (IA-REQ-027). The token it mints is
    /// a NEW confirmation token, not the invitation's — an earlier version of this test inspected the invitation
    /// token and so would have passed over a confirmation envelope that was never written at all.
    /// </summary>
    [Test]
    public async Task Registering_from_an_invitation_writes_a_confirmation_envelope_holding_its_own_new_token()
    {
        await IssuedInvitationAsync();
        var invitationToken = TestApp.RawTokenAt(0);
        ResetToAnonymous();

        (await TestApp.SendAsync(new RegisterInvitedUserCommand(invitationToken, ValidPassword))).IsSuccess.ShouldBeTrue();

        var confirmationToken = TestApp.RawTokenAt(1);
        confirmationToken.ShouldNotBe(invitationToken);
        var messages = await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>();
        messages.Count.ShouldBe(2, "the invitation's delivery and the confirmation's are separate messages");
        messages.ShouldAllBe(message => !message.Payload.Contains(confirmationToken) && !message.Payload.Contains(invitationToken));

        var secrets = await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>();
        var confirmation = secrets.Single(secret => new VersionedTokenHasher().Verify(confirmationToken, secret.VersionedHash));
        confirmation.Ciphertext.ShouldNotBeNull();
        confirmation.Ciphertext!.ShouldNotContain(confirmationToken);
        confirmation.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        confirmation.Status.ShouldBe(Domain.IdentityAccess.Outbox.OutboxSecretStatus.Pending);
        messages.ShouldContain(message => message.Id == confirmation.OutboxMessageId);
    }

    /// <summary>
    /// The whole point of IA-REQ-016's public half, end to end: registering and then confirming leaves a confirmed
    /// identity, an invitation still pending, and no membership. Confirmation is not acceptance — acceptance is a
    /// separate authenticated request, and nothing before it may grant access to the organization.
    /// </summary>
    [Test]
    public async Task Registering_then_confirming_leaves_a_confirmed_identity_with_the_offer_still_pending_and_no_membership()
    {
        var (email, token) = await IssuedInvitationAsync();
        var membershipsBefore = await TestApp.CountAsync<TenantMembership>();
        ResetToAnonymous();

        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.RawTokenAt(1)))).IsSuccess.ShouldBeTrue();

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        identity.EmailConfirmed.ShouldBeTrue("confirmation is what the registration flow exists to reach");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(membershipsBefore, "confirming an identity is not accepting an invitation");
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(InvitationStatus.Pending, "the offer still has to be accepted by the signed-in recipient");
        invitation.AcceptedByIdentityId.ShouldBeNull();
    }

    [Test]
    public async Task An_expired_invited_confirmation_is_invalid_and_creates_no_membership_or_activation()
    {
        var (email, token) = await IssuedInvitationAsync();
        var membershipsBefore = await TestApp.CountAsync<TenantMembership>();
        ResetToAnonymous();
        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        var confirmation = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>())
            .Single(message => message.Type == "identity.invitation.confirmation.requested");
        await ExpireConfirmationAsync(confirmation.Id);
        var confirmationToken = TestApp.RawTokenAt(1);

        var first = await TestApp.SendAsync(new ConfirmEmailCommand(confirmationToken));
        var replay = await TestApp.SendAsync(new ConfirmEmailCommand(confirmationToken));

        first.IsFailure.ShouldBeTrue();
        first.Error!.Code.ShouldBe("invalid_confirmation");
        replay.IsFailure.ShouldBeTrue();
        replay.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(membershipsBefore);
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
        (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>())
            .Single(secret => secret.OutboxMessageId == confirmation.Id)
            .Status.ShouldBe(Domain.IdentityAccess.Outbox.OutboxSecretStatus.Expired);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_failed_invited_confirmation_is_invalid_over_http_and_preserves_the_pending_offer()
    {
        var (email, token) = await IssuedInvitationAsync();
        var membershipsBefore = await TestApp.CountAsync<TenantMembership>();
        ResetToAnonymous();
        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        var confirmation = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>())
            .Single(message => message.Type == "identity.invitation.confirmation.requested");
        var confirmationToken = TestApp.RawTokenAt(1);
        await TestApp.FailConfirmationSecretAsync(confirmation.Id);
        var failedBefore = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>())
            .Single(secret => secret.OutboxMessageId == confirmation.Id);
        var auditsBefore = await TestApp.CountAsync<Domain.IdentityAccess.Auditing.AuditEvent>();

        var host = $"https://failed-invited-confirmation-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/confirm-email",
            new { token = confirmationToken },
            antiforgery);
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var problem = await IdentityHttpHarness.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_confirmation");
        problem.GetProperty("instance").GetString().ShouldBe("/api/identity/confirm-email");
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(membershipsBefore);
        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.AcceptedByIdentityId.ShouldBeNull();
        var failedAfter = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>())
            .Single(secret => secret.OutboxMessageId == confirmation.Id);
        failedAfter.Status.ShouldBe(Domain.IdentityAccess.Outbox.OutboxSecretStatus.Failed);
        failedAfter.TerminalReason.ShouldBe(failedBefore.TerminalReason);
        failedAfter.CompletedAt.ShouldBe(failedBefore.CompletedAt);
        (await TestApp.CountAsync<Domain.IdentityAccess.Auditing.AuditEvent>()).ShouldBe(auditsBefore);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    /// <summary>
    /// IA-REQ-016 does not merely say the existing branch stays silent — it says the address receives a generic
    /// sign-in notice. Without it the branch also does strictly less work than the other, which is the shape a
    /// timing oracle takes.
    /// </summary>
    [Test]
    public async Task An_address_that_already_has_an_account_receives_a_generic_notice_carrying_no_token()
    {
        var (email, token) = await IssuedInvitationAsync();
        await InvitationScenario.SeedConfirmedRecipientAsync(email);
        var before = await TestApp.CountAsync<Domain.IdentityAccess.Outbox.OutboxMessage>();
        ResetToAnonymous();

        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, "AnotherPassword1!"))).IsSuccess.ShouldBeTrue();

        var messages = await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>();
        messages.Count.ShouldBe(before + 1, "the owner of the address is told, generically, that someone tried to register with it");
        var notice = messages.Single(message => message.Type == "identity.invitation.signin.notice.requested");
        notice.Payload.ShouldNotContain(token);
        notice.Payload.ShouldNotContain(email, Case.Insensitive);
        (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>())
            .ShouldAllBe(secret => secret.OutboxMessageId != notice.Id, "a generic notice carries no secret at all");
    }

    /// <summary>
    /// An address the invitation aggregate accepts and ASP.NET Identity refuses. Answering 400 here would say the
    /// token was real, while an unknown token answers 202 — a token oracle reachable by anyone who can guess an
    /// address the two rule sets disagree about.
    /// </summary>
    [Test]
    public async Task An_address_identity_refuses_stays_as_neutral_as_an_unknown_token()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        // Canonical for the aggregate: composed, lowercase, no whitespace. Rejected by the default user validator,
        // whose allowed set is ASCII letters, digits and a handful of punctuation.
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, "josé@example.test", [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        ResetToAnonymous();

        var real = await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(0), ValidPassword));
        var unknown = await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(8), ValidPassword));

        real.IsSuccess.ShouldBe(unknown.IsSuccess, "a real token must not be distinguishable from an unknown one");
        real.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<ApplicationUser>()).ShouldNotContain(user => user.Email == "josé@example.test");
        (await InvitationScenario.SingleInvitationAsync()).Status.ShouldBe(InvitationStatus.Pending);
    }

    /// <summary>
    /// Two invitees submitting the same address concurrently: one creates the identity, the other loses the race
    /// between the lookup and the create. The loser must stay neutral rather than surface the collision.
    /// </summary>
    [Test]
    public async Task A_lost_race_to_create_the_identity_stays_neutral()
    {
        var (email, token) = await IssuedInvitationAsync();
        ResetToAnonymous();
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => SendFromIndependentScopeAsync(new RegisterInvitedUserCommand(token, ValidPassword), barrier)),
            Task.Run(() => SendFromIndependentScopeAsync(new RegisterInvitedUserCommand(token, ValidPassword), barrier)));

        results.ShouldAllBe(result => result.IsSuccess, "the loser of the race is as neutral as the winner");
        (await TestApp.ListAsync<ApplicationUser>()).Count(user => user.Email == email).ShouldBe(1);
    }

    [Test]
    public async Task Invited_confirmation_is_audited_once_without_a_tenant_and_rollback_is_atomic()
    {
        var (email, token) = await IssuedInvitationAsync();
        ResetToAnonymous();
        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        var command = new ConfirmEmailCommand(TestApp.RawTokenAt(1));
        TestApp.ForceConfirmationRollbackAfterPersistedEffects();
        await Should.ThrowAsync<InvalidOperationException>(() => TestApp.SendAsync(command));
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>()).ShouldNotContain(item => item.EventType == "identity.confirmed");
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        var audit = (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>()).Single(item => item.EventType == "identity.confirmed");
        audit.TenantId.ShouldBeNull();
        audit.ActorId.ShouldBe((await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).Id);
        audit.CorrelationId.ShouldStartWith("confirmation-");
        (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxSecret>()).Count(item => item.Status == Domain.IdentityAccess.Outbox.OutboxSecretStatus.Consumed).ShouldBe(1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Real_invitation_registration_messages_have_registered_delivery_handlers(bool existing)
    {
        var (email, token) = await IssuedInvitationAsync();
        if (existing) await InvitationScenario.SeedConfirmedRecipientAsync(email);
        ResetToAnonymous();
        (await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        var type = existing ? "identity.invitation.signin.notice.requested" : "identity.invitation.confirmation.requested";
        var message = (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).Single(item => item.Type == type);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        scope.ServiceProvider.GetServices<CleanArchitecture.Infrastructure.Outbox.IOutboxDeliveryHandler>()
            .ShouldContain(handler => handler.MessageType == message.Type);
        var sink = new RegistrationDeliverySink();
        var dispatcher = new OutboxDispatcher(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IOutboxSecretReader>(), scope.ServiceProvider.GetServices<IOutboxDeliveryHandler>(), TimeProvider.System, sink,
            scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.IdentityAccess.Lifecycle.IRecoveryAdmission>(), FunctionalTestMetrics.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        (await TestApp.ListAsync<Domain.IdentityAccess.Outbox.OutboxMessage>()).Single(item => item.Id == message.Id)
            .Status.ShouldBe(Domain.IdentityAccess.Outbox.OutboxMessageStatus.Delivered);
        var delivered = sink.Messages.Single(item => item.Key == message.Id.ToString());
        delivered.Recipient.ToUpperInvariant().ShouldBe(email.ToUpperInvariant());
        delivered.Body.ShouldContain("https://app.example.test/");
        if (existing)
        {
            delivered.Body.ShouldNotContain(token);
            delivered.Body.ShouldNotContain("invitation", Case.Insensitive);
            delivered.Body.ShouldNotContain("#token=");
        }
        else Uri.UnescapeDataString(delivered.Body).ShouldContain(TestApp.RawTokenAt(1));
    }

    private sealed class RegistrationDeliverySink : IIdentityEmailSender
    {
        public List<(string Recipient, string Body, string Key)> Messages { get; } = [];
        public Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
        {
            Messages.Add((recipient, body, idempotencyKey));
            return Task.FromResult(new EmailDeliveryReceipt(true, Guid.NewGuid().ToString(), false));
        }
    }

    private static async Task<Application.Common.Models.Result> SendFromIndependentScopeAsync(RegisterInvitedUserCommand command, Barrier barrier)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
        return await sender.Send(command);
    }

    private static async Task<(string Email, string Token)> IssuedInvitationAsync()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        return (email, TestApp.RawTokenAt(0));
    }

    private static async Task ExpireConfirmationAsync(Guid outboxMessageId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlAsync(
            $"UPDATE outbox_secrets SET \"ExpiresAt\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"OutboxMessageId\" = {outboxMessageId}");
    }

    /// <summary>The invitee holds no session and no tenant: this request is reached anonymously.</summary>
    private static void ResetToAnonymous()
    {
        TestApp.SetUserId(null);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(false);
    }
}
