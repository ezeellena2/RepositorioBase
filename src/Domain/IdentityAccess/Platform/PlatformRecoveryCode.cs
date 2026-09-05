namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// One single-use recovery code, stored only as a hash (IA-REQ-041). It is reachable only through its enrollment,
/// which is what keeps the set of codes something an identity has exactly one of.
/// </summary>
public sealed class PlatformRecoveryCode : BaseEntity<Guid>
{
    private PlatformRecoveryCode() { }

    public PlatformMfaEnrollmentId EnrollmentId { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    internal static PlatformRecoveryCode Create(PlatformMfaEnrollmentId enrollmentId, string codeHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        if (enrollmentId.IsEmpty)
        {
            throw new ArgumentException("Recovery codes belong to an enrollment.", nameof(enrollmentId));
        }

        return new PlatformRecoveryCode
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            CodeHash = codeHash,
            CreatedAt = now
        };
    }

    /// <summary>Single use: a code that has already been spent cannot be spent again.</summary>
    internal void Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException("A recovery code can only be used once.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        ConsumedAt = now;
    }

    public bool IsAvailable => ConsumedAt is null;
}
