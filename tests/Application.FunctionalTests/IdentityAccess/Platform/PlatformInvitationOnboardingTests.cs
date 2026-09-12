using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// IA-REQ-041's onboarding half. Registration and confirmation get a Platform invitee as far as being a
/// confirmed, signed-in identity and no further — every one of these tests also asserts what did *not* happen,
/// because the whole point of the gate chain is that authority arrives last.
/// <para>
/// Each outcome is measured against a real invitation and against a token that resolves to nothing, because the
/// flow is neutral: it must not reveal whether the token is live, nor — given a live one — whether the address
/// already has an account.
/// </para>
/// </summary>
public sealed class PlatformInvitationOnboardingTests : TestBase
{
    private const string ValidPassword = PlatformScenario.ValidPassword;
    private const string PolicyViolatingPassword = "short";

    [Test]
    public async Task Registering_creates_the_confirmable_identity_with_the_submitted_password_and_never_a_membership()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();

        var result = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        result.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        identity.EmailConfirmed.ShouldBeFalse("registration issues confirmation; it does not grant it.");
        identity.PasswordHash.ShouldNotBeNull("the submitted password is the account's credential.");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0, "no Platform membership exists before the MFA gates.");
        (await PlatformScenario.SingleInvitationAsync()).Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
    }

    /// <summary>The submitted password is the real credential, which is what proves no default was generated.</summary>
    [Test]
    public async Task The_created_identity_can_only_sign_in_with_the_password_that_was_submitted()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        (await PlatformIdentityProbe.PasswordMatchesAsync(identity, ValidPassword)).ShouldBeTrue();
        (await PlatformIdentityProbe.PasswordMatchesAsync(identity, "SomethingElse1!")).ShouldBeFalse();
    }

    /// <summary>
    /// The password is judged before anything is read, so a policy violation answers the same way for a real token
    /// and an unknown one. A refusal that only a live token could produce would be an oracle for the token itself.
    /// </summary>
    [Test]
    public async Task A_password_the_policy_refuses_is_refused_identically_for_a_real_and_an_unknown_token()
    {
        var (_, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        TestApp.ResetConfirmationTokenHashInvocationCount();
        var identitiesBefore = await TestApp.CountAsync<ApplicationUser>();
        var messagesBefore = await TestApp.CountAsync<OutboxMessage>();
        var secretsBefore = await TestApp.CountAsync<OutboxSecret>();

        var real = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PolicyViolatingPassword));
        var unknown = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(UnknownToken(), PolicyViolatingPassword));

        real.IsFailure.ShouldBeTrue();
        real.Error!.Code.ShouldBe("validation_failed");
        real.Error.ValidationErrors.Keys.ShouldBe(["password"]);
        real.Error.ValidationErrors["password"].ShouldBe([
            "Passwords must be at least 12 characters.",
            "Passwords must have at least one non alphanumeric character.",
            "Passwords must have at least one digit ('0'-'9').",
            "Passwords must have at least one uppercase ('A'-'Z')."
        ]);
        string.Join(' ', real.Error.ValidationErrors["password"]).ShouldNotContain(PolicyViolatingPassword);
        unknown.IsFailure.ShouldBeTrue();
        unknown.Error!.Code.ShouldBe(real.Error.Code);
        unknown.Error.Category.ShouldBe(real.Error.Category);
        unknown.Error.ValidationErrors["password"].ShouldBe(real.Error.ValidationErrors["password"]);
        TestApp.ConfirmationTokenHashInvocationCount.ShouldBe(0, "a refused password is decided before the token is read at all");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identitiesBefore, "a refused password creates nothing.");
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(messagesBefore);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(secretsBefore);
        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        invitation.BoundIdentityId.ShouldBeNull();
    }

    [Test]
    public async Task An_unknown_token_answers_exactly_as_a_live_one_and_creates_nothing()
    {
        await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();

        var result = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(UnknownToken(), ValidPassword));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    /// <summary>
    /// The existing-identity branch. Asserting the stored hash is unchanged is what proves "ignored" rather than
    /// "overwritten": holding this token is not proof of owning that account (IA-REQ-041).
    /// </summary>
    [Test]
    public async Task Registering_when_the_identity_already_exists_ignores_the_credentials_and_cannot_take_over_the_account()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync(language: "es");
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, ValidPassword, "en");
        var before = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var storedHash = before.PasswordHash;
        PlatformScenario.RunAnonymously();

        var result = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, "AnotherPassword1!"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "an existing address is never registered twice.");
        var after = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        after.PasswordHash.ShouldBe(storedHash, "submitted credentials must not overwrite an account the caller may not own.");
        after.PreferredLanguage.ShouldBe("en", "the neutral existing-account branch cannot overwrite a preference.");
        after.EmailConfirmed.ShouldBeTrue("an already-confirmed identity is not un-confirmed by an invitation.");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    /// <summary>
    /// Both branches write exactly one message, so the work they cost is indistinguishable from outside. Only the
    /// missing-identity branch seals a confirmation token, because only it has an unconfirmed address to confirm.
    /// </summary>
    [Test]
    public async Task Each_branch_writes_one_message_and_only_the_new_identity_gets_a_sealed_confirmation()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var messages = await PlatformScenario.MessagesAsync();
        messages.Count.ShouldBe(1);
        messages[0].Type.ShouldBe("platform.invitation.confirmation.requested");
        messages[0].Payload.ShouldNotContain(token, Case.Insensitive);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(1);

        await TestApp.ResetState();
        var (existingEmail, existingToken) = await PlatformScenario.PendingInvitationAsync();
        await IdentityHttpHarness.SeedConfirmedUserAsync(existingEmail, ValidPassword);
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(existingToken, ValidPassword));

        var existingMessages = await PlatformScenario.MessagesAsync();
        existingMessages.Count.ShouldBe(1);
        existingMessages[0].Type.ShouldBe("platform.invitation.signin.notice.requested");
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0, "an existing identity is sent no token at all.");
    }

    /// <summary>
    /// The dead end R5 closes. An invitee who lets their confirmation expire has an identity that exists and is
    /// unusable: the expired token is refused, an unconfirmed identity cannot sign in, and the only branch that
    /// ever minted a confirmation was the one that created the identity — which by definition cannot run again.
    /// Answering the invitation a second time now reissues the confirmation, and nothing else.
    /// </summary>
    [Test]
    public async Task Answering_again_reissues_the_confirmation_for_an_identity_that_never_confirmed()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var first = (await PlatformScenario.MessagesAsync()).Single(message => message.Type == "platform.invitation.confirmation.requested");
        var storedHash = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).PasswordHash;
        await PlatformScenario.ExpireConfirmationEnvelopeAsync();

        var result = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, "AnotherPassword1!"));

        result.IsSuccess.ShouldBeTrue();
        var confirmations = (await PlatformScenario.MessagesAsync())
            .Where(message => message.Type == "platform.invitation.confirmation.requested").ToArray();
        confirmations.Length.ShouldBe(2, "the pending identity is sent another confirmation.");
        var reissued = await PlatformScenario.SealedTokenAsync(confirmations[^1].Id);
        reissued.ShouldNotBeNullOrWhiteSpace();

        // The account itself is untouched: no credential change, no membership, and the recipient binding is
        // still the invitation's own rather than anything the caller supplied.
        var after = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        after.PasswordHash.ShouldBe(storedHash, "a reissue is not a password reset.");
        after.EmailConfirmed.ShouldBeFalse();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await PlatformScenario.SingleInvitationAsync()).BoundIdentityId.ShouldBe(after.Id);

        // And it completes: the reissued token confirms, which is the whole point of issuing it.
        (await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(reissued))).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeTrue();
        first.Id.ShouldNotBe(confirmations[^1].Id);
    }

    /// <summary>
    /// One usable confirmation at a time, including when the superseded one was already delivered. Confirmation
    /// accepts a pending or a delivered envelope, so retiring only the pending ones would leave the recipient
    /// holding two working links — and the older one names the same identity, so it would still confirm.
    /// </summary>
    [Test]
    public async Task Reissuing_retires_a_confirmation_that_was_already_delivered()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var first = (await PlatformScenario.MessagesAsync()).Single(message => message.Type == "platform.invitation.confirmation.requested");
        var firstToken = await PlatformScenario.SealedTokenAsync(first.Id);
        await PlatformScenario.MarkConfirmationSecretDeliveredAsync(first.Id);

        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var superseded = (await TestApp.ListAsync<OutboxSecret>()).Single(secret => secret.OutboxMessageId == first.Id);
        superseded.Status.ShouldBe(OutboxSecretStatus.Expired);
        superseded.Ciphertext.ShouldBeNull("a retired envelope stops being readable.");
        (await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(firstToken))).IsFailure.ShouldBeTrue();
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
    }

    /// <summary>
    /// The branch is chosen on whether the identity is usable, not on whether it exists. A confirmed account is
    /// still told only that it can sign in, and is still sent no token of any kind.
    /// </summary>
    [Test]
    public async Task Answering_again_for_a_confirmed_identity_still_only_sends_the_neutral_notice()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, ValidPassword);
        PlatformScenario.RunAnonymously();

        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var messages = await PlatformScenario.MessagesAsync();
        messages.ShouldAllBe(message => message.Type == "platform.invitation.signin.notice.requested");
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0, "a confirmed identity is sent no token at all.");
    }

    [Test]
    public async Task Registration_binds_the_invitation_to_the_identity_that_answered_it()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();

        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.BoundIdentityId.ShouldBe(identity.Id);
        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Pending, "binding is not acceptance.");
    }

    [Test]
    public async Task Confirmation_confirms_the_identity_consumes_the_secret_and_still_creates_no_membership()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var confirmationToken = await PlatformScenario.SealedTokenAsync((await PlatformScenario.MessagesAsync()).Single().Id);

        var result = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeTrue();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Consumed);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0, "confirmation never activates Platform membership.");
        (await PlatformScenario.SingleInvitationAsync()).Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
    }

    [Test]
    public async Task Replaying_a_confirmation_is_idempotent_and_writes_nothing_further()
    {
        var (_, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var confirmationToken = await PlatformScenario.SealedTokenAsync((await PlatformScenario.MessagesAsync()).Single().Id);
        await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));
        var messagesAfterFirst = (await PlatformScenario.MessagesAsync()).Count;

        var replay = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        replay.IsSuccess.ShouldBeTrue();
        (await PlatformScenario.MessagesAsync()).Count.ShouldBe(messagesAfterFirst);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    [Test]
    public async Task An_expired_confirmation_is_invalid_on_every_spend_and_has_no_onboarding_effects()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var message = (await PlatformScenario.MessagesAsync()).Single();
        var confirmationToken = await PlatformScenario.SealedTokenAsync(message.Id);
        await PlatformScenario.ExpireConfirmationEnvelopeAsync();

        var first = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));
        var replay = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        first.IsFailure.ShouldBeTrue();
        first.Error!.Code.ShouldBe("invalid_confirmation");
        replay.IsFailure.ShouldBeTrue();
        replay.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Expired);
        (await PlatformScenario.SingleInvitationAsync()).Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_failed_platform_confirmation_is_invalid_over_http_and_preserves_all_durable_state()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword))).IsSuccess.ShouldBeTrue();
        var message = (await PlatformScenario.MessagesAsync()).Single();
        var confirmationToken = await PlatformScenario.SealedTokenAsync(message.Id);
        await TestApp.FailConfirmationSecretAsync(message.Id);
        var identityBefore = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var invitationBefore = await PlatformScenario.SingleInvitationAsync();
        var secretBefore = (await TestApp.ListAsync<OutboxSecret>()).Single();
        var usersBefore = await TestApp.CountAsync<ApplicationUser>();
        var messagesBefore = await TestApp.CountAsync<OutboxMessage>();
        var secretsBefore = await TestApp.CountAsync<OutboxSecret>();
        var membershipsBefore = await TestApp.CountAsync<TenantMembership>();
        var auditsBefore = await TestApp.CountAsync<Domain.IdentityAccess.Auditing.AuditEvent>();

        var host = $"https://failed-platform-confirmation-{Guid.NewGuid():N}.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(FunctionalTestSetup.HttpClient, host);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/platform/invitations/confirm",
            new { confirmationToken },
            antiforgery);
        using var response = await FunctionalTestSetup.HttpClient.SendAsync(request);

        var problem = await IdentityHttpHarness.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_confirmation");
        problem.GetProperty("instance").GetString().ShouldBe("/api/platform/invitations/confirm");
        var identityAfter = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        identityAfter.Id.ShouldBe(identityBefore.Id);
        identityAfter.Status.ShouldBe(identityBefore.Status);
        identityAfter.EmailConfirmed.ShouldBeFalse();
        var invitationAfter = await PlatformScenario.SingleInvitationAsync();
        invitationAfter.Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        invitationAfter.BoundIdentityId.ShouldBe(invitationBefore.BoundIdentityId);
        var secretAfter = (await TestApp.ListAsync<OutboxSecret>()).Single();
        secretAfter.Status.ShouldBe(OutboxSecretStatus.Failed);
        secretAfter.TerminalReason.ShouldBe(secretBefore.TerminalReason);
        secretAfter.CompletedAt.ShouldBe(secretBefore.CompletedAt);
        secretAfter.VersionedHash.ShouldBe(secretBefore.VersionedHash);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(usersBefore);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(messagesBefore);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(secretsBefore);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(membershipsBefore);
        (await TestApp.CountAsync<Domain.IdentityAccess.Auditing.AuditEvent>()).ShouldBe(auditsBefore);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_confirmation_for_an_invitation_that_is_no_longer_pending_is_invalid_and_leaves_the_secret_unspent()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var message = (await PlatformScenario.MessagesAsync()).Single();
        var confirmationToken = await PlatformScenario.SealedTokenAsync(message.Id);
        await CancelInvitationAsync();

        var result = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Pending);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    [Test]
    public async Task A_confirmation_whose_invitation_binding_moved_is_invalid_and_has_no_onboarding_effects()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var message = (await PlatformScenario.MessagesAsync()).Single();
        var confirmationToken = await PlatformScenario.SealedTokenAsync(message.Id);
        var stranger = await IdentityHttpHarness.SeedConfirmedUserAsync(
            $"stranger-{Guid.NewGuid():N}@example.test",
            ValidPassword);
        await MoveInvitationBindingAsync(stranger);

        var result = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email).EmailConfirmed.ShouldBeFalse();
        (await TestApp.ListAsync<OutboxSecret>()).Single().Status.ShouldBe(OutboxSecretStatus.Pending);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.ListAsync<Domain.IdentityAccess.Auditing.AuditEvent>())
            .ShouldNotContain(item => item.EventType == "identity.confirmed");
    }

    /// <summary>
    /// A confirmation belongs to the offer whose envelope was sealed with it, and to the identity that answered
    /// that offer. Neither comes from the caller, so a token can only ever complete its own onboarding.
    /// </summary>
    [Test]
    public async Task A_confirmation_completes_only_the_identity_its_envelope_names()
    {
        var (firstEmail, firstToken) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(firstToken, ValidPassword));
        var confirmation = await PlatformScenario.SealedTokenAsync((await PlatformScenario.MessagesAsync()).Single().Id);

        var stranger = await IdentityHttpHarness.SeedConfirmedUserAsync($"stranger-{Guid.NewGuid():N}@example.test", ValidPassword);

        (await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmation))).IsSuccess.ShouldBeTrue();

        var identities = await TestApp.ListAsync<ApplicationUser>();
        identities.Single(user => user.Email == firstEmail).EmailConfirmed.ShouldBeTrue();
        identities.Single(user => user.Id == stranger).Id.ShouldBe(stranger, "nobody else was touched.");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    [Test]
    public async Task An_unknown_confirmation_token_is_refused_without_confirming_anything()
    {
        var (_, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));

        var result = await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(UnknownToken()));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_confirmation");
        (await TestApp.ListAsync<ApplicationUser>()).Single().EmailConfirmed.ShouldBeFalse();
    }

    private static async Task CancelInvitationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = await context.PlatformAdminInvitations.SingleAsync();
        invitation.Cancel(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    private static async Task MoveInvitationBindingAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitationId = (await context.PlatformAdminInvitations.SingleAsync()).Id.Value;
        await context.Database.ExecuteSqlAsync(
            $"UPDATE \"PlatformAdminInvitations\" SET \"BoundIdentityId\" = {identityId} WHERE \"Id\" = {invitationId}");
    }

    private static string UnknownToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
