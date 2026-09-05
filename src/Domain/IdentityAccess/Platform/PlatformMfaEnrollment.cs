namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// One identity's Platform second factor (IA-REQ-041). It holds the encrypted TOTP secret, the hashed one-time
/// recovery codes, and the evidence of when the factor was last proved — which is what a Platform mutation checks
/// when it requires a recent step-up.
/// <para>
/// The gates are ordered by the status and cannot be taken out of order: codes cannot be acknowledged before the
/// factor is proved, a membership cannot activate before they are, and an enrollment that never reached
/// <see cref="PlatformMfaEnrollmentStatus.Active"/> can never satisfy a step-up. Keeping that order here rather
/// than in the handlers is what stops a later endpoint from skipping one.
/// </para>
/// <para>
/// Step-up evidence names the session it was produced in. Binding it to the identity alone would let a second,
/// never-stepped-up session inherit the freshness of one that had — which is precisely the window step-up exists
/// to close.
/// </para>
/// </summary>
public sealed class PlatformMfaEnrollment : BaseEntity<PlatformMfaEnrollmentId>
{
    private readonly List<PlatformRecoveryCode> _recoveryCodes = new();

    private PlatformMfaEnrollment() { }

    public Guid IdentityId { get; private set; }

    public string EncryptedSecret { get; private set; } = string.Empty;

    public PlatformMfaEnrollmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset? RecoveryAcknowledgedAt { get; private set; }

    /// <summary>When the factor was last proved, which is the freshness a Platform mutation reads.</summary>
    public DateTimeOffset? LastVerifiedAt { get; private set; }

    public Guid? LastVerifiedSessionId { get; private set; }

    public IReadOnlyCollection<PlatformRecoveryCode> RecoveryCodes => _recoveryCodes.AsReadOnly();

    /// <summary>
    /// Begins an enrollment with its secret and its recovery codes together, because the contract hands both to
    /// the recipient in one answer (SPEC section 6). Holding a code before the factor is proved grants nothing:
    /// a code is only ever spendable against a completed enrollment, and this one is not one yet.
    /// </summary>
    public static PlatformMfaEnrollment Begin(Guid identityId, string encryptedSecret, IEnumerable<string> codeHashes, DateTimeOffset now)
    {
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret);
        ArgumentNullException.ThrowIfNull(codeHashes);

        var enrollment = new PlatformMfaEnrollment
        {
            Id = PlatformMfaEnrollmentId.New(),
            IdentityId = identityId,
            EncryptedSecret = encryptedSecret,
            Status = PlatformMfaEnrollmentStatus.Pending,
            CreatedAt = now
        };
        enrollment.SetRecoveryCodes(codeHashes, now);
        return enrollment;
    }

    /// <summary>
    /// Replaces the secret of an enrollment nobody has proved yet. Restarting is the recipient losing their
    /// authenticator before finishing, which must not require an administrator; restarting one that is already
    /// verified or active would be a way to swap a working factor without proving the current one.
    /// </summary>
    public void Restart(string encryptedSecret, IEnumerable<string> codeHashes, DateTimeOffset now)
    {
        if (Status != PlatformMfaEnrollmentStatus.Pending)
        {
            throw new InvalidOperationException("Only an unverified enrollment can be restarted.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedSecret);
        ArgumentNullException.ThrowIfNull(codeHashes);
        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        EncryptedSecret = encryptedSecret;
        SetRecoveryCodes(codeHashes, now);
    }

    /// <summary>
    /// Records that a code from this secret was accepted. Replaying it for a verified enrollment is the caller
    /// retrying and simply refreshes the step-up evidence, which is also what a later step-up does.
    /// </summary>
    public void Verify(Guid sessionId, DateTimeOffset now)
    {
        if (Status == PlatformMfaEnrollmentStatus.Pending)
        {
            if (now < CreatedAt)
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }

            Status = PlatformMfaEnrollmentStatus.Verified;
            VerifiedAt = now;
        }

        RecordStepUp(sessionId, now);
    }

    /// <summary>
    /// Replaces the whole set. It is private because the only two moments a set may be written are the two the
    /// recipient is shown one: beginning an enrollment and restarting an unverified one. Reissuing at any other
    /// point would silently invalidate codes the owner has already stored.
    /// </summary>
    private void SetRecoveryCodes(IEnumerable<string> codeHashes, DateTimeOffset now)
    {
        var hashes = codeHashes.ToArray();
        if (hashes.Length == 0)
        {
            throw new ArgumentException("An enrollment must carry at least one recovery code.", nameof(codeHashes));
        }

        _recoveryCodes.Clear();
        foreach (var hash in hashes)
        {
            _recoveryCodes.Add(PlatformRecoveryCode.Create(Id, hash, now));
        }
    }

    /// <summary>The last gate: the owner says they have kept the codes, and only then may authority follow.</summary>
    public void AcknowledgeRecoveryCodes(DateTimeOffset now)
    {
        if (Status == PlatformMfaEnrollmentStatus.Active)
        {
            return;
        }

        if (Status != PlatformMfaEnrollmentStatus.Verified)
        {
            throw new InvalidOperationException("Recovery codes are acknowledged only after the factor is verified.");
        }

        if (_recoveryCodes.Count == 0)
        {
            throw new InvalidOperationException("There are no recovery codes to acknowledge.");
        }

        Status = PlatformMfaEnrollmentStatus.Active;
        RecoveryAcknowledgedAt = now;
    }

    public void RecordStepUp(Guid sessionId, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Step-up evidence names the session it was produced in.", nameof(sessionId));
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        LastVerifiedAt = now;
        LastVerifiedSessionId = sessionId;
    }

    /// <summary>
    /// Whether this session has ever proved the factor. It is what reading Platform requires: a session that only
    /// presented a password has proved one thing, and the operational directories are not for it (IA-REQ-045).
    /// <para>
    /// Proof deliberately outlives the freshness window. Re-prompting an administrator every fifteen minutes to
    /// keep reading a directory would train them to type a code without reading what it is for, which is the
    /// opposite of what the window exists to achieve — so freshness is asked only of a change.
    /// </para>
    /// </summary>
    public bool HasProvedFactor(Guid sessionId) =>
        Status == PlatformMfaEnrollmentStatus.Active &&
        LastVerifiedSessionId == sessionId &&
        LastVerifiedAt is not null;

    /// <summary>
    /// Whether this session has proved the factor recently enough to make a Platform change. An enrollment that
    /// never completed can never satisfy it, however recently a code was accepted.
    /// </summary>
    public bool HasRecentStepUp(Guid sessionId, DateTimeOffset now, TimeSpan window) =>
        HasProvedFactor(sessionId) &&
        LastVerifiedAt is { } verified &&
        verified <= now &&
        now - verified <= window;

    /// <summary>Spends one recovery code, identified by the hash a caller's submitted code produced.</summary>
    public bool TryConsumeRecoveryCode(string codeHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return false;
        }

        var code = _recoveryCodes.Find(candidate =>
            candidate.IsAvailable && string.Equals(candidate.CodeHash, codeHash, StringComparison.Ordinal));
        if (code is null)
        {
            return false;
        }

        code.Consume(now);
        return true;
    }
}
