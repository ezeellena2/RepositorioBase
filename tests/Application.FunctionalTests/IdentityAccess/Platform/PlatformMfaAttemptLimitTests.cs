using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Infrastructure.Platform;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The bound on second-factor guessing (IA-REQ-041).
/// <para>
/// A six-digit code has a million values and three windows are accepted at once, so an endpoint that answers
/// "wrong code" indefinitely is a search anyone with a stolen password can finish. Password guessing already had
/// two controls; this is the one code submission had none of.
/// </para>
/// <para>
/// Every test here spends the budget through the real requests rather than the limiter directly, because what
/// matters is that the handler consults it before comparing — a limiter wired only to the failure path would
/// count nothing when the attacker's guess is right.
/// </para>
/// </summary>
public sealed class PlatformMfaAttemptLimitTests : TestBase
{
    private const string WrongCode = "000000";

    [Test]
    public async Task Wrong_codes_are_refused_as_wrong_until_the_budget_runs_out_and_then_as_a_rate_limit()
    {
        var invitee = await EnrolledAsync();

        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            var refused = await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
            refused.Error!.Code.ShouldBe("invalid_invitation", "a wrong code is still only a wrong code.");
        }

        var exhausted = await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
        exhausted.Error!.Code.ShouldBe("rate_limit_exceeded");
        exhausted.Error.RetryAfterSeconds.ShouldNotBeNull();
        exhausted.Error.RetryAfterSeconds!.Value.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The one thing a limiter here must not be: per-session. Signing in again is a single request, so a budget
    /// that reset with the session would be no budget at all — which is why the key is the identity.
    /// </summary>
    [Test]
    public async Task A_new_session_does_not_restore_the_budget()
    {
        var invitee = await EnrolledAsync();
        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
        }

        // Everything a fresh sign-in changes, and nothing else: same identity, new session.
        TestApp.SetSessionId(Guid.NewGuid());

        var exhausted = await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
        exhausted.Error!.Code.ShouldBe("rate_limit_exceeded");
    }

    /// <summary>Even a correct code is refused while the budget is spent; the gate runs before the comparison.</summary>
    [Test]
    public async Task An_exhausted_budget_refuses_the_right_code_too()
    {
        var invitee = await EnrolledAsync();
        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
        }

        var correct = await TestApp.SendAsync(
            new VerifyPlatformMfaEnrollmentCommand(invitee.Token, PlatformMfaTests.TotpCode(invitee.SharedKey)));

        correct.Error!.Code.ShouldBe("rate_limit_exceeded");
    }

    /// <summary>One person's mistakes are not another's. A shared counter would be a denial of service by proxy.</summary>
    [Test]
    public async Task One_identity_exhausting_its_budget_leaves_another_untouched()
    {
        var first = await EnrolledAsync();
        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(first.Token, WrongCode));
        }

        var second = await EnrolledAsync(seedRoles: false, isOwner: false);

        var verified = await TestApp.SendAsync(
            new VerifyPlatformMfaEnrollmentCommand(second.Token, PlatformMfaTests.TotpCode(second.SharedKey)));

        verified.IsSuccess.ShouldBeTrue();

        PlatformScenario.RunAs(first.IdentityId);
        (await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(first.Token, WrongCode)))
            .Error!.Code.ShouldBe("rate_limit_exceeded", "the other identity's success must not spend or clear this one.");
    }

    /// <summary>An accepted code clears the budget, so an owner who mistyped is not held by their own typing.</summary>
    [Test]
    public async Task An_accepted_code_clears_what_the_failures_spent()
    {
        var invitee = await EnrolledAsync();
        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget - 1; attempt++)
        {
            await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode));
        }

        (await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, PlatformMfaTests.TotpCode(invitee.SharedKey))))
            .IsSuccess.ShouldBeTrue();

        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            (await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(invitee.Token, WrongCode)))
                .Error!.Code.ShouldBe("invalid_invitation", "the budget started over when the code was accepted.");
        }
    }

    /// <summary>
    /// Step-up shares the budget because it accepts the same secret. Bounding one route and not the other would
    /// only move the guessing across, and an activated administrator can reach step-up with a password alone.
    /// </summary>
    [Test]
    public async Task Step_up_spends_the_same_budget_as_enrollment_verification()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        for (var attempt = 0; attempt < PlatformMfaAttemptLimiter.Budget; attempt++)
        {
            (await TestApp.SendAsync(new StepUpPlatformMfaCommand(WrongCode))).Error!.Code.ShouldBe("invalid_session");
        }

        var exhausted = await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(owner.SharedKey)));
        exhausted.Error!.Code.ShouldBe("rate_limit_exceeded");
        exhausted.Error.RetryAfterSeconds!.Value.ShouldBeGreaterThan(0);
    }

    /// <summary>An invitee past every gate but the code, which is the state the limit is about.</summary>
    private static async Task<PlatformMfaTests.Invitee> EnrolledAsync(bool seedRoles = true, bool isOwner = true)
    {
        var invitee = await PlatformMfaTests.ConfirmedInviteeAsync(seedRoles, isOwner);
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitee.Token));
        enrollment.IsSuccess.ShouldBeTrue();
        return invitee with { SharedKey = enrollment.Value!.SharedKey };
    }
}
