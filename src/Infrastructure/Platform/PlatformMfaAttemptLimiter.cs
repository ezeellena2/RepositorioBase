using System.Collections.Concurrent;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

namespace CleanArchitecture.Infrastructure.Platform;

/// <summary>
/// Bounds failed second-factor submissions per identity (IA-REQ-041).
/// <para>
/// The budget is five attempts per fifteen minutes, matching the bootstrap recovery limiter it is modelled on.
/// The SPEC fixes no number, so this is a choice: enough that an administrator mistyping a code, or one whose
/// authenticator clock has drifted a window, is not locked out of their own console; few enough that guessing a
/// six-digit code is not a matter of persistence. An accepted code clears it, so only failures accumulate.
/// </para>
/// <para>
/// It is in-process, which is the same posture the login limiters already have and the same residual limitation:
/// a restart clears the budget, and several instances multiply it by their count. That is written down rather
/// than hidden, and the port exists so a durable implementation can replace this one without touching a handler.
/// Neither weakness is reachable by a caller — nothing they send changes the key, and they cannot cause a restart.
/// </para>
/// </summary>
public sealed class PlatformMfaAttemptLimiter(TimeProvider timeProvider) : IPlatformMfaAttemptLimiter
{
    /// <summary>Public because it is the policy, and a test that restated it could drift from the gate it checks.</summary>
    public const int Budget = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Attempts = new();

    public Task<PlatformMfaAttemptLease> TryAcquireAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var attempts = Attempts.GetOrAdd(identityId, _ => new Queue<DateTimeOffset>());

        lock (attempts)
        {
            while (attempts.Count > 0 && now - attempts.Peek() >= Window)
            {
                attempts.Dequeue();
            }

            if (attempts.Count >= Budget)
            {
                var retryAfter = (int)Math.Ceiling((Window - (now - attempts.Peek())).TotalSeconds);
                return Task.FromResult(new PlatformMfaAttemptLease(false, Math.Max(retryAfter, 1)));
            }

            attempts.Enqueue(now);
            return Task.FromResult(PlatformMfaAttemptLease.Granted);
        }
    }

    public Task ResetAsync(Guid identityId, CancellationToken cancellationToken)
    {
        Attempts.TryRemove(identityId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears every budget. It is public because the budget outlives a test — it is process state, not database
    /// state — and a suite where one test inherits another's attempts is one that fails by order.
    /// </summary>
    public static void Reset() => Attempts.Clear();
}
