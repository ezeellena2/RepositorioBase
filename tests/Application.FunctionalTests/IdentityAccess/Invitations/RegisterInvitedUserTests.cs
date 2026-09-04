using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;

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
        var (email, token) = await IssuedInvitationAsync();
        ResetToAnonymous();

        var result = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, ValidPassword));

        result.IsSuccess.ShouldBeTrue();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        identity.EmailConfirmed.ShouldBeFalse("registration issues confirmation; it does not grant it");
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
        var (email, token) = await IssuedInvitationAsync();
        await InvitationScenario.SeedConfirmedRecipientAsync(email);
        var before = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var storedHash = before.PasswordHash;
        var identityCount = await TestApp.CountAsync<ApplicationUser>();
        ResetToAnonymous();

        var result = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, "AnotherPassword1!"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(identityCount, "an existing address is never registered twice");
        var after = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        after.PasswordHash.ShouldBe(storedHash, "submitted credentials must not overwrite an account the caller may not own");
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

        var live = await TestApp.SendAsync(new RegisterInvitedUserCommand(token, PolicyViolatingPassword));
        var dead = await TestApp.SendAsync(new RegisterInvitedUserCommand(TestApp.RawTokenAt(9), PolicyViolatingPassword));

        live.IsFailure.ShouldBeTrue();
        live.Error!.Code.ShouldBe("invalid_invitation");
        dead.IsFailure.ShouldBeTrue();
        dead.Error!.Code.ShouldBe(live.Error.Code, "a weak password must not double as a token-validity oracle");
        dead.Error.Category.ShouldBe(live.Error.Category);
        TestApp.ConfirmationTokenHashInvocationCount.ShouldBe(0, "a refused password is decided before the token is read at all");
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

    private static async Task<(string Email, string Token)> IssuedInvitationAsync()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var email = $"invitee-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new InviteMemberCommand(organization.TenantId, email, [organization.RoleId]))).IsSuccess.ShouldBeTrue();
        return (email, TestApp.RawTokenAt(0));
    }

    /// <summary>The invitee holds no session and no tenant: this request is reached anonymously.</summary>
    private static void ResetToAnonymous()
    {
        TestApp.SetUserId(null);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(false);
    }
}
