namespace CleanArchitecture.Domain.IdentityAccess.Platform;

public readonly record struct PlatformMfaEnrollmentId
{
    private PlatformMfaEnrollmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static PlatformMfaEnrollmentId New() => new(Guid.NewGuid());

    public static PlatformMfaEnrollmentId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Platform MFA enrollment identifiers cannot be empty.", nameof(value))
        : new PlatformMfaEnrollmentId(value);
}
