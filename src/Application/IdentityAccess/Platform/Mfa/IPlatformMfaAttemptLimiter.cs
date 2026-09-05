namespace CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

/// <summary>One attempt's worth of budget, or the wait a caller who has none left must observe.</summary>
public sealed record PlatformMfaAttemptLease(bool IsAcquired, int RetryAfterSeconds)
{
    public static PlatformMfaAttemptLease Granted { get; } = new(true, 0);
}

/// <summary>
/// Bounds how many times a code may be submitted for one identity's second factor (IA-REQ-041).
/// <para>
/// A six-digit code has a million values and three windows are accepted at once, so an unbounded endpoint is a
/// guessing game with a fixed cost. Password guessing already has two controls — the account lockout and the
/// login transport windows — and code guessing had none, which left the second factor as the weaker of the two
/// for a caller who had already obtained a password.
/// </para>
/// <para>
/// The key is the identity behind the validated session, never anything the caller submits and never the session
/// itself: a budget keyed on the session would reset every time the caller signed in again, which is one request
/// away. Nothing here reads or returns state about the identity, so an exhausted budget tells a caller only that
/// they must wait.
/// </para>
/// </summary>
public interface IPlatformMfaAttemptLimiter
{
    Task<PlatformMfaAttemptLease> TryAcquireAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>Clears the budget once a code is accepted, so a successful owner is never held by their own typos.</summary>
    Task ResetAsync(Guid identityId, CancellationToken cancellationToken);
}
