using System.Threading.RateLimiting;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Fixed-window limiter with zero queue whose window is measured by the injected <see cref="TimeProvider"/>,
/// because <see cref="FixedWindowRateLimiterOptions"/> cannot be driven by a test clock. A rejected lease
/// carries <see cref="MetadataName.RetryAfter"/> with the time left in the current window.
/// </summary>
public sealed class TimeProviderFixedWindowRateLimiter : RateLimiter
{
    private static readonly RateLimitLease Acquired = new WindowLease(true, null);

    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private DateTimeOffset? _windowStart;
    private int _used;
    private long _successfulLeases;
    private long _failedLeases;

    public TimeProviderFixedWindowRateLimiter(int permitLimit, TimeSpan window, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _permitLimit = permitLimit;
        _window = window;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Null while the current window still holds consumed permits, so the partitioned limiter never evicts a
    /// partially spent window; once the window has elapsed the limiter is idle and may be evicted.
    /// </summary>
    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_lock)
            {
                if (_windowStart is not { } start)
                {
                    return TimeSpan.Zero;
                }

                var idleSince = start + _window;
                var now = _timeProvider.GetUtcNow();
                return now >= idleSince ? now - idleSince : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_lock)
        {
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = _permitLimit - UsedAt(_timeProvider.GetUtcNow()),
                CurrentQueuedCount = 0,
                TotalSuccessfulLeases = _successfulLeases,
                TotalFailedLeases = _failedLeases
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Acquire(permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        new(Acquire(permitCount));

    private RateLimitLease Acquire(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, _permitLimit);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_windowStart is not { } start || now >= start + _window)
            {
                start = now;
                _windowStart = now;
                _used = 0;
            }

            if (permitCount == 0)
            {
                return _used < _permitLimit ? Acquired : new WindowLease(false, start + _window - now);
            }

            if (_used + permitCount <= _permitLimit)
            {
                _used += permitCount;
                _successfulLeases++;
                return Acquired;
            }

            _failedLeases++;
            return new WindowLease(false, start + _window - now);
        }
    }

    private int UsedAt(DateTimeOffset now) =>
        _windowStart is { } start && now < start + _window ? _used : 0;

    private sealed class WindowLease(bool isAcquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal))
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
