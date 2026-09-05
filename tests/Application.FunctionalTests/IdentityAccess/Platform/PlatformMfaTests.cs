using System.Security.Cryptography;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The MFA gates end to end (IA-REQ-041). What these prove is mostly what does not happen: no Platform membership
/// exists until the very last gate, and each gate refuses to run before the one before it has.
/// </summary>
public sealed class PlatformMfaTests : TestBase
{
    [Test]
    public async Task Enrolling_hands_over_the_key_and_the_codes_once_and_grants_nothing()
    {
        var invitee = await ConfirmedInviteeAsync();

        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        enrollment.IsSuccess.ShouldBeTrue();
        enrollment.Value!.SharedKey.ShouldNotBeNullOrWhiteSpace();
        enrollment.Value.ProvisioningUri.ShouldStartWith("otpauth://totp/");
        enrollment.Value.RecoveryCodes.Count.ShouldBe(10);
        enrollment.Value.RecoveryCodes.ShouldBeUnique();

        var stored = (await TestApp.ListAsync<PlatformMfaEnrollment>()).Single();
        stored.Status.ShouldBe(PlatformMfaEnrollmentStatus.Pending);
        stored.EncryptedSecret.ShouldNotContain(enrollment.Value.SharedKey, Case.Insensitive);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0, "enrolling grants nothing.");
    }

    /// <summary>A code the authenticator never produced is not a proof, so the factor stays unproved.</summary>
    [Test]
    public async Task A_wrong_code_does_not_verify_the_factor()
    {
        var invitee = await ConfirmedInviteeAsync();
        await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        var result = await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, "000000"));

        result.IsFailure.ShouldBeTrue();
        (await TestApp.ListAsync<PlatformMfaEnrollment>()).Single().Status.ShouldBe(PlatformMfaEnrollmentStatus.Pending);
    }

    [Test]
    public async Task Verifying_proves_the_factor_and_still_grants_nothing()
    {
        var invitee = await ConfirmedInviteeAsync();
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        var result = await TestApp.SendAsync(
            new VerifyPlatformMfaEnrollmentCommand(invitee.Token, TotpCode(enrollment.Value!.SharedKey)));

        result.IsSuccess.ShouldBeTrue();
        var stored = (await TestApp.ListAsync<PlatformMfaEnrollment>()).Single();
        stored.Status.ShouldBe(PlatformMfaEnrollmentStatus.Verified);
        stored.LastVerifiedAt.ShouldNotBeNull();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0, "verifying grants nothing either.");
    }

    /// <summary>The gate order is the whole point: acknowledgement cannot stand in for verification.</summary>
    [Test]
    public async Task Acknowledging_before_verifying_is_refused_and_activates_nothing()
    {
        var invitee = await ConfirmedInviteeAsync();
        await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        var result = await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(invitee.Token));

        result.IsFailure.ShouldBeTrue();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await PlatformScenario.SingleInvitationAsync()).Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
    }

    [Test]
    public async Task Acknowledging_after_verifying_activates_the_membership_and_consumes_the_invitation()
    {
        var invitee = await CompletedGatesAsync();

        var membership = (await TestApp.ListAsync<TenantMembership>()).ShouldHaveSingleItem();
        membership.IdentityId.ShouldBe(invitee.IdentityId);
        membership.Status.ShouldBe(MembershipStatus.Active);
        (await TestApp.ListAsync<PlatformMfaEnrollment>()).Single().Status.ShouldBe(PlatformMfaEnrollmentStatus.Active);
        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Accepted);
        invitation.BoundIdentityId.ShouldBe(invitee.IdentityId);
    }

    /// <summary>
    /// The activation transaction is the whole chain becoming authority at once. Replaying it must not produce a
    /// second membership, and the consumed invitation is what stops it.
    /// </summary>
    [Test]
    public async Task Replaying_the_last_gate_creates_no_second_membership()
    {
        var invitee = await CompletedGatesAsync();

        var replay = await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(invitee.Token));

        replay.IsFailure.ShouldBeTrue("a consumed invitation no longer admits anyone.");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1);
    }

    /// <summary>
    /// Without a role the ceremony created there is nothing to grant, and inventing one at the moment of granting
    /// is exactly what IA-REQ-042 forbids — so activation refuses rather than improvises.
    /// </summary>
    [Test]
    public async Task Activation_is_refused_when_the_platform_carries_no_system_role_to_grant()
    {
        var invitee = await ConfirmedInviteeAsync(seedRoles: false);
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));
        await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, TotpCode(enrollment.Value!.SharedKey)));

        var result = await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(invitee.Token));

        result.IsFailure.ShouldBeTrue();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    [Test]
    public async Task A_completed_enrollment_cannot_be_restarted_by_a_request_that_proved_nothing()
    {
        var invitee = await CompletedGatesAsync();

        var result = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        result.IsFailure.ShouldBeTrue("a working factor is never swapped without proving the current one.");
    }

    /// <summary>Step-up is what makes a later Platform change require a fresh proof rather than an old session.</summary>
    [Test]
    public async Task Step_up_re_proves_the_factor_for_an_administrator_who_already_holds_authority()
    {
        var invitee = await CompletedGatesAsync();

        var result = await TestApp.SendAsync(new StepUpPlatformMfaCommand(TotpCode(invitee.SharedKey)));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<PlatformMfaEnrollment>()).Single().LastVerifiedAt.ShouldNotBeNull();
    }

    /// <summary>An unfinished enrollment is not authority, so there is nothing for a step-up to refresh.</summary>
    [Test]
    public async Task Step_up_is_refused_for_an_enrollment_that_never_completed()
    {
        var invitee = await ConfirmedInviteeAsync();
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));

        var result = await TestApp.SendAsync(new StepUpPlatformMfaCommand(TotpCode(enrollment.Value!.SharedKey)));

        result.IsFailure.ShouldBeTrue();
    }

    [Test]
    public async Task Step_up_is_refused_for_a_wrong_code()
    {
        await CompletedGatesAsync();

        var result = await TestApp.SendAsync(new StepUpPlatformMfaCommand("000000"));

        result.IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// One pending owner offer at a time (IA-REQ-040). The database holds it, so a second bootstrap or a racing
    /// recovery cannot leave two live owner invitations behind.
    /// </summary>
    [Test]
    public async Task A_second_pending_owner_invitation_cannot_exist()
    {
        await PlatformScenario.PendingInvitationAsync(isOwner: true);

        var second = await Should.ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => PlatformScenario.PendingInvitationAsync(isOwner: true));

        second.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
    }

    internal sealed record Invitee(Guid IdentityId, string Email, string Token, string SharedKey);

    internal static async Task<Invitee> ConfirmedInviteeAsync(bool seedRoles = true, bool isOwner = true)
    {
        if (seedRoles) await PlatformScenario.SeedPlatformRolesAsync();
        var (email, token) = await PlatformScenario.PendingInvitationAsync(isOwner);
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        // The newest confirmation rather than the only one: a test may set up more than one invitee, and this
        // helper has to keep working when it does.
        var confirmationToken = await PlatformScenario.SealedTokenAsync(
            (await PlatformScenario.MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        PlatformScenario.RunAs(identity.Id);
        return new Invitee(identity.Id, email, token, string.Empty);
    }

    internal static async Task<Invitee> CompletedGatesAsync()
    {
        var invitee = await ConfirmedInviteeAsync();
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));
        await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, TotpCode(enrollment.Value!.SharedKey)));
        (await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(invitee.Token))).IsSuccess.ShouldBeTrue();
        return invitee with { SharedKey = enrollment.Value.SharedKey };
    }

    /// <summary>
    /// What a real authenticator would show for this key at this moment. The test computes RFC 6238 itself rather
    /// than asking the implementation, so a broken implementation cannot agree with itself and pass.
    /// </summary>
    internal static string TotpCode(string sharedKey, DateTimeOffset? at = null)
    {
        var secret = Base32Decode(sharedKey);
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        Span<byte> buffer = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer, counter);
        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(secret, buffer, mac);
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(value.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var character in value.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(char.ToUpperInvariant(character));
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
            bitsLeft -= 8;
        }

        return [.. bytes];
    }

}
