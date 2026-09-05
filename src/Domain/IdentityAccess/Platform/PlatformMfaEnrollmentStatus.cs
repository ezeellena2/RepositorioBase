namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// The gates, in the order IA-REQ-041 fixes them. An enrollment is <see cref="Pending"/> while the secret exists
/// but nobody has proved they hold it, <see cref="Verified"/> once a code from it has been accepted, and
/// <see cref="Active"/> only once the recovery codes have been acknowledged — which is the last gate before a
/// Platform membership may become active.
/// </summary>
public enum PlatformMfaEnrollmentStatus
{
    Pending,
    Verified,
    Active
}
