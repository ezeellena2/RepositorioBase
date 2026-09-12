using System.Diagnostics;
using CleanArchitecture.Application.Common.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.OutboxWorker;

public sealed class OutboxWorkerOptions
{
    public const string SectionName = "OutboxWorker";

    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumFailureBackoff { get; set; } = TimeSpan.FromMinutes(1);
    public int UnhealthyAfterIntervals { get; set; } = 60;
}

/// <summary>The last evidence that a complete dispatcher pass returned successfully.</summary>
public sealed class OutboxWorkerHeartbeat
{
    private const long NoSuccessfulPass = long.MinValue;
    private readonly TimeProvider _timeProvider;
    private readonly long _startedAtUtcTicks;
    private readonly long _unhealthyAfterTicks;
    private long _lastSuccessfulPassUtcTicks = NoSuccessfulPass;

    public OutboxWorkerHeartbeat(TimeProvider timeProvider, IOptions<OutboxWorkerOptions> options)
    {
        _timeProvider = timeProvider;
        _startedAtUtcTicks = timeProvider.GetUtcNow().UtcTicks;
        _unhealthyAfterTicks = MultiplyAndClamp(
            options.Value.IdleInterval.Ticks,
            options.Value.UnhealthyAfterIntervals);
    }

    public DateTimeOffset? LastSuccessfulPassAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulPassUtcTicks);
            return ticks == NoSuccessfulPass ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public bool IsHealthy
    {
        get
        {
            var lastSuccess = Interlocked.Read(ref _lastSuccessfulPassUtcTicks);
            var reference = lastSuccess == NoSuccessfulPass ? _startedAtUtcTicks : lastSuccess;
            var now = _timeProvider.GetUtcNow().UtcTicks;
            return now <= reference || now - reference <= _unhealthyAfterTicks;
        }
    }

    internal void MarkSuccessfulPass() =>
        Interlocked.Exchange(ref _lastSuccessfulPassUtcTicks, _timeProvider.GetUtcNow().UtcTicks);

    private static long MultiplyAndClamp(long intervalTicks, int intervals)
    {
        if (intervalTicks <= 0 || intervals <= 0) return 0;
        return intervalTicks > TimeSpan.MaxValue.Ticks / intervals
            ? TimeSpan.MaxValue.Ticks
            : intervalTicks * intervals;
    }
}

public sealed class OutboxWorkerHealthCheck(OutboxWorkerHeartbeat heartbeat) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(heartbeat.IsHealthy
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
}

public readonly record struct OutboxPassOutcome(bool ShouldStop, TimeSpan Delay)
{
    internal static OutboxPassOutcome Stop { get; } = new(true, TimeSpan.Zero);
}

/// <summary>Owns scheduling policy while the hosted service supplies the scoped dispatcher pass.</summary>
public sealed class OutboxWorkerRuntime
{
    public static readonly string ActivitySourceName = typeof(Worker).Assembly.GetName().Name
        ?? "CleanArchitecture.OutboxWorker";

    private const string PassOperation = "outbox.dispatch";
    private static readonly ActivitySource PassActivities = new(ActivitySourceName);
    private readonly TimeProvider _timeProvider;
    private readonly OutboxWorkerOptions _options;
    private readonly OutboxWorkerHeartbeat _heartbeat;
    private readonly ILogger<Worker> _logger;
    private readonly object _failureGate = new();
    private TimeSpan _nextFailureDelay;

    public OutboxWorkerRuntime(
        TimeProvider timeProvider,
        IOptions<OutboxWorkerOptions> options,
        OutboxWorkerHeartbeat heartbeat,
        ILogger<Worker> logger)
    {
        _timeProvider = timeProvider;
        _options = options.Value;
        _heartbeat = heartbeat;
        _logger = logger;
        _nextFailureDelay = _options.IdleInterval;
    }

    public async Task RunAsync(
        Func<CancellationToken, Task<int>> dispatch,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        while (!stoppingToken.IsCancellationRequested)
        {
            var outcome = await RunPassAsync(dispatch, stoppingToken);
            if (outcome.ShouldStop) return;

            if (outcome.Delay == TimeSpan.Zero)
            {
                // A synchronously-completing test double must not turn backlog draining into a tight CPU loop.
                await Task.Yield();
                continue;
            }

            try
            {
                await Task.Delay(outcome.Delay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task<OutboxPassOutcome> RunPassAsync(
        Func<CancellationToken, Task<int>> dispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (cancellationToken.IsCancellationRequested) return OutboxPassOutcome.Stop;

        using var activity = PassActivities.StartActivity(PassOperation, ActivityKind.Internal);
        try
        {
            var delivered = await dispatch(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            ResetFailureBackoff();
            _heartbeat.MarkSuccessfulPass();
            return new OutboxPassOutcome(false, delivered == 0 ? _options.IdleInterval : TimeSpan.Zero);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OutboxPassOutcome.Stop;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogSafeFailure(SafeFailure.Describe(exception, PassOperation));
            return new OutboxPassOutcome(false, TakeFailureDelay());
        }
    }

    private TimeSpan TakeFailureDelay()
    {
        lock (_failureGate)
        {
            var current = _nextFailureDelay;
            var maximumTicks = _options.MaximumFailureBackoff.Ticks;
            var doubledTicks = current.Ticks >= maximumTicks / 2
                ? maximumTicks
                : current.Ticks * 2;
            _nextFailureDelay = TimeSpan.FromTicks(Math.Min(doubledTicks, maximumTicks));
            return current;
        }
    }

    private void ResetFailureBackoff()
    {
        lock (_failureGate)
        {
            _nextFailureDelay = _options.IdleInterval;
        }
    }
}
