using CleanArchitecture.Domain.IdentityAccess.Platform;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

/// <summary>
/// The MFA gates, in the order IA-REQ-041 fixes them. Almost every test here asserts a refusal: the value of the
/// chain is that none of its links can be taken out of order, so what must not happen is the behaviour.
/// </summary>
public sealed class PlatformMfaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StepUpWindow = TimeSpan.FromMinutes(15);
    private static readonly string[] Codes = ["hash-1", "hash-2", "hash-3"];

    private static PlatformMfaEnrollment Pending() =>
        PlatformMfaEnrollment.Begin(Guid.NewGuid(), "ciphertext", Codes, Now);

    private static PlatformMfaEnrollment Verified(Guid sessionId)
    {
        var enrollment = Pending();
        enrollment.Verify(sessionId, Now);
        return enrollment;
    }

    private static PlatformMfaEnrollment Active(Guid sessionId)
    {
        var enrollment = Verified(sessionId);
        enrollment.AcknowledgeRecoveryCodes(Now);
        return enrollment;
    }

    [Test]
    public void Beginning_an_enrollment_stores_only_ciphertext_and_proves_nothing_yet()
    {
        var identityId = Guid.NewGuid();

        var enrollment = PlatformMfaEnrollment.Begin(identityId, "ciphertext", Codes, Now);

        enrollment.IdentityId.ShouldBe(identityId);
        enrollment.EncryptedSecret.ShouldBe("ciphertext");
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Pending);
        enrollment.VerifiedAt.ShouldBeNull();
        enrollment.LastVerifiedAt.ShouldBeNull();
        enrollment.RecoveryCodes.Count.ShouldBe(3, "the contract hands the codes over with the secret.");
        enrollment.RecoveryCodes.ShouldAllBe(code => code.IsAvailable);
    }

    [Test]
    public void An_enrollment_requires_an_identity_and_a_secret()
    {
        Should.Throw<ArgumentException>(() => PlatformMfaEnrollment.Begin(Guid.Empty, "ciphertext", Codes, Now));
        Should.Throw<ArgumentException>(() => PlatformMfaEnrollment.Begin(Guid.NewGuid(), "  ", Codes, Now));
        Should.Throw<ArgumentException>(() => PlatformMfaEnrollment.Begin(Guid.NewGuid(), "ciphertext", [], Now));
    }

    /// <summary>
    /// Holding a code before the factor is proved grants nothing, because a code is only spendable against a
    /// completed enrollment — but acknowledging them is what closes the chain, and that cannot come first.
    /// </summary>
    [Test]
    public void Recovery_codes_cannot_be_acknowledged_before_the_factor_is_verified()
    {
        var enrollment = Pending();

        Should.Throw<InvalidOperationException>(() => enrollment.AcknowledgeRecoveryCodes(Now));
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Pending);
        enrollment.HasRecentStepUp(Guid.NewGuid(), Now, StepUpWindow).ShouldBeFalse();
    }

    [Test]
    public void The_gates_complete_only_in_order_and_acknowledgement_is_idempotent()
    {
        var sessionId = Guid.NewGuid();
        var enrollment = Pending();

        enrollment.Verify(sessionId, Now);
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Verified);
        enrollment.VerifiedAt.ShouldBe(Now);

        enrollment.RecoveryCodes.Count.ShouldBe(3);
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Verified, "holding codes is not acknowledging them.");

        enrollment.AcknowledgeRecoveryCodes(Now);
        enrollment.AcknowledgeRecoveryCodes(Now.AddMinutes(1));
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Active);
        enrollment.RecoveryAcknowledgedAt.ShouldBe(Now);
    }

    /// <summary>An unfinished enrollment can never satisfy a step-up, however recently a code was accepted.</summary>
    [Test]
    public void Only_a_completed_enrollment_can_satisfy_a_step_up()
    {
        var sessionId = Guid.NewGuid();
        var verified = Verified(sessionId);

        verified.LastVerifiedAt.ShouldBe(Now);
        verified.HasRecentStepUp(sessionId, Now, StepUpWindow).ShouldBeFalse("the gates are not complete.");

        var active = Active(sessionId);
        active.HasRecentStepUp(sessionId, Now, StepUpWindow).ShouldBeTrue();
    }

    /// <summary>
    /// The window is what makes it a step-up rather than a one-time ceremony, and the session binding is what
    /// stops a second session inheriting freshness it never earned.
    /// </summary>
    [Test]
    public void A_step_up_expires_and_belongs_to_the_session_that_produced_it()
    {
        var sessionId = Guid.NewGuid();
        var enrollment = Active(sessionId);

        enrollment.HasRecentStepUp(sessionId, Now.Add(StepUpWindow), StepUpWindow).ShouldBeTrue();
        enrollment.HasRecentStepUp(sessionId, Now.Add(StepUpWindow).AddSeconds(1), StepUpWindow).ShouldBeFalse();
        enrollment.HasRecentStepUp(Guid.NewGuid(), Now, StepUpWindow).ShouldBeFalse("another session never stepped up.");

        enrollment.RecordStepUp(sessionId, Now.AddHours(1));
        enrollment.HasRecentStepUp(sessionId, Now.AddHours(1), StepUpWindow).ShouldBeTrue();
    }

    [Test]
    public void Step_up_evidence_must_name_a_session()
    {
        var enrollment = Active(Guid.NewGuid());

        Should.Throw<ArgumentException>(() => enrollment.RecordStepUp(Guid.Empty, Now));
    }

    [Test]
    public void A_recovery_code_can_be_spent_once()
    {
        var enrollment = Active(Guid.NewGuid());

        enrollment.TryConsumeRecoveryCode("hash-2", Now).ShouldBeTrue();
        enrollment.TryConsumeRecoveryCode("hash-2", Now).ShouldBeFalse("a recovery code is single use.");
        enrollment.TryConsumeRecoveryCode("hash-unknown", Now).ShouldBeFalse();
        enrollment.RecoveryCodes.Count(code => code.IsAvailable).ShouldBe(2);
    }

    /// <summary>
    /// Losing an authenticator before finishing must not need an administrator; swapping a working factor without
    /// proving the current one must not be possible at all.
    /// </summary>
    [Test]
    public void Only_an_unverified_enrollment_can_be_restarted()
    {
        var enrollment = Pending();
        enrollment.Restart("other-ciphertext", ["hash-9"], Now);
        enrollment.EncryptedSecret.ShouldBe("other-ciphertext");
        enrollment.RecoveryCodes.Count.ShouldBe(1, "a restart replaces the codes it replaced the secret with.");

        var active = Active(Guid.NewGuid());
        Should.Throw<InvalidOperationException>(() => active.Restart("swapped", ["hash-9"], Now));
        active.RecoveryCodes.Count.ShouldBe(3);
    }
}
