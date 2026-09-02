using System.Threading.RateLimiting;
using CleanArchitecture.Web.Infrastructure.Identity;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class TimeProviderFixedWindowRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestCase(false)]
    [TestCase(true)]
    public async Task Permits_within_the_window_are_granted_and_the_next_request_is_rejected_immediately_with_retry_after(bool useAsync)
    {
        var clock = new ControlledTimeProvider(Start);
        using var limiter = new TimeProviderFixedWindowRateLimiter(3, TimeSpan.FromMinutes(5), clock);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await AcquireAsync(limiter, useAsync)).IsAcquired.ShouldBeTrue($"attempt {attempt + 1} must be granted");
        }

        var rejected = await AcquireAsync(limiter, useAsync);
        rejected.IsAcquired.ShouldBeFalse();
        rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBe(TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(2));
        var stillRejected = await AcquireAsync(limiter, useAsync);
        stillRejected.IsAcquired.ShouldBeFalse();
        stillRejected.TryGetMetadata(MetadataName.RetryAfter, out var remaining).ShouldBeTrue();
        remaining.ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Test]
    public void Window_resets_only_when_the_injected_clock_passes_the_window()
    {
        var clock = new ControlledTimeProvider(Start);
        using var limiter = new TimeProviderFixedWindowRateLimiter(2, TimeSpan.FromMinutes(15), clock);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(14));
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();
    }

    [Test]
    public void Idle_duration_is_null_while_the_window_holds_consumed_permits_and_grows_after_it_elapses()
    {
        var clock = new ControlledTimeProvider(Start);
        using var limiter = new TimeProviderFixedWindowRateLimiter(3, TimeSpan.FromMinutes(5), clock);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();

        limiter.IdleDuration.ShouldBeNull();
        clock.Advance(TimeSpan.FromMinutes(4));
        limiter.IdleDuration.ShouldBeNull();
        clock.Advance(TimeSpan.FromMinutes(1));
        limiter.IdleDuration.ShouldBe(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(1));
        limiter.IdleDuration.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Test]
    public void Zero_permit_probe_reports_availability_without_consuming_permits()
    {
        var clock = new ControlledTimeProvider(Start);
        using var limiter = new TimeProviderFixedWindowRateLimiter(2, TimeSpan.FromMinutes(5), clock);

        limiter.AttemptAcquire(0).IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire(0).IsAcquired.ShouldBeFalse();
    }

    [Test]
    public void Statistics_report_remaining_permits_and_lease_counts()
    {
        var clock = new ControlledTimeProvider(Start);
        using var limiter = new TimeProviderFixedWindowRateLimiter(3, TimeSpan.FromMinutes(5), clock);
        limiter.AttemptAcquire();
        limiter.AttemptAcquire();

        var statistics = limiter.GetStatistics().ShouldNotBeNull();
        statistics.CurrentAvailablePermits.ShouldBe(1);
        statistics.TotalSuccessfulLeases.ShouldBe(2);
        statistics.TotalFailedLeases.ShouldBe(0);

        limiter.AttemptAcquire();
        limiter.AttemptAcquire();
        limiter.GetStatistics()!.TotalFailedLeases.ShouldBe(1);
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(0);
    }

    private static async ValueTask<RateLimitLease> AcquireAsync(RateLimiter limiter, bool useAsync) =>
        useAsync ? await limiter.AcquireAsync() : limiter.AttemptAcquire();

    private sealed class ControlledTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
