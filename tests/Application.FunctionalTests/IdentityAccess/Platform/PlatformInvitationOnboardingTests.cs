using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;

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

        var real = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PolicyViolatingPassword));
        var unknown = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(UnknownToken(), PolicyViolatingPassword));

        real.IsFailure.ShouldBeTrue();
        real.Error!.Code.ShouldBe(unknown.Error!.Code);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0, "a refused password creates nothing.");
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
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, ValidPassword);
        var before = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var storedHash = before.PasswordHash;
        PlatformScenario.RunAnonymously();

        var result = await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, "AnotherPassword1!"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1, "an existing address is never registered twice.");
        var after = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        after.PasswordHash.ShouldBe(storedHash, "submitted credentials must not overwrite an account the caller may not own.");
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

    private static string UnknownToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
