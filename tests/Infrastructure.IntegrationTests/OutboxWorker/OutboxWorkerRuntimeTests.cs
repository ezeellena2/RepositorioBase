extern alias OutboxWorkerAssembly;

using System.Diagnostics;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxWorkerHeartbeat = OutboxWorkerAssembly::CleanArchitecture.OutboxWorker.OutboxWorkerHeartbeat;
using OutboxWorkerOptions = OutboxWorkerAssembly::CleanArchitecture.OutboxWorker.OutboxWorkerOptions;
using OutboxWorkerRuntime = OutboxWorkerAssembly::CleanArchitecture.OutboxWorker.OutboxWorkerRuntime;
using OutboxWorkerServiceCollectionExtensions = OutboxWorkerAssembly::CleanArchitecture.OutboxWorker.OutboxWorkerServiceCollectionExtensions;
using Worker = OutboxWorkerAssembly::CleanArchitecture.OutboxWorker.Worker;

namespace CleanArchitecture.Infrastructure.IntegrationTests.OutboxWorker;

public sealed class OutboxWorkerRuntimeTests
{
    private static readonly DateTimeOffset Origin = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Consecutive_failures_back_off_to_the_cap_and_a_success_resets_the_sequence()
    {
        var harness = CreateHarness();

        foreach (var expected in new[] { 5, 10, 20, 40, 60, 60 })
        {
            var failed = await harness.Runtime.RunPassAsync(
                _ => Task.FromException<int>(new InvalidOperationException("private provider details")),
                CancellationToken.None);

            failed.ShouldStop.ShouldBeFalse();
            failed.Delay.ShouldBe(TimeSpan.FromSeconds(expected));
        }

        var succeeded = await harness.Runtime.RunPassAsync(_ => Task.FromResult(0), CancellationToken.None);
        succeeded.Delay.ShouldBe(TimeSpan.FromSeconds(5));

        var failedAfterSuccess = await harness.Runtime.RunPassAsync(
            _ => Task.FromException<int>(new InvalidOperationException("private provider details")),
            CancellationToken.None);
        failedAfterSuccess.Delay.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Empty_and_nonempty_successes_mark_the_heartbeat_and_choose_the_expected_delay()
    {
        var harness = CreateHarness();

        var empty = await harness.Runtime.RunPassAsync(_ => Task.FromResult(0), CancellationToken.None);
        empty.Delay.ShouldBe(TimeSpan.FromSeconds(5));
        harness.Heartbeat.LastSuccessfulPassAt.ShouldBe(Origin);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var delivered = await harness.Runtime.RunPassAsync(_ => Task.FromResult(3), CancellationToken.None);
        delivered.Delay.ShouldBe(TimeSpan.Zero);
        harness.Heartbeat.LastSuccessfulPassAt.ShouldBe(Origin.AddSeconds(1));
    }

    [Test]
    public async Task Every_pass_has_an_activity_and_failures_log_only_the_safe_exception_projection()
    {
        var stopped = new List<Activity>();
        using var listener = ListenToWorkerActivities(stopped);
        var harness = CreateHarness();

        await harness.Runtime.RunPassAsync(_ => Task.FromResult(0), CancellationToken.None);
        await harness.Runtime.RunPassAsync(
            _ => Task.FromException<int>(new InvalidOperationException("token=private")),
            CancellationToken.None);

        stopped.Count.ShouldBe(2);
        stopped.ShouldAllBe(activity => activity.OperationName == "outbox.dispatch");
        stopped.ShouldAllBe(activity => activity.Source.Name == typeof(Worker).Assembly.GetName().Name);
        stopped[0].Status.ShouldBe(ActivityStatusCode.Ok);
        stopped[1].Status.ShouldBe(ActivityStatusCode.Error);

        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Text.ShouldContain(typeof(InvalidOperationException).FullName!);
        entry.Text.ShouldNotContain("token=private");
        entry.Exception.ShouldBeNull();
    }

    [Test]
    public async Task Cancellation_during_dispatch_stops_without_a_failure_log_or_heartbeat()
    {
        var harness = CreateHarness();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pass = harness.Runtime.RunPassAsync(async cancellationToken =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }, cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        var outcome = await pass;
        outcome.ShouldStop.ShouldBeTrue();
        harness.Logger.Entries.ShouldBeEmpty();
        harness.Heartbeat.LastSuccessfulPassAt.ShouldBeNull();
    }

    [Test]
    public async Task Cancellation_during_the_idle_delay_stops_the_loop_and_disposes_its_timer()
    {
        var clock = new TrackingTimeProvider(Origin);
        var heartbeat = new OutboxWorkerHeartbeat(clock, Options.Create(new OutboxWorkerOptions()));
        var logger = new CapturingLogger<Worker>();
        var runtime = new OutboxWorkerRuntime(clock, Options.Create(new OutboxWorkerOptions()), heartbeat, logger);
        using var cancellation = new CancellationTokenSource();
        var passes = 0;

        var loop = runtime.RunAsync(_ => Task.FromResult(++passes == 1 ? 0 : throw new InvalidOperationException()), cancellation.Token);
        await clock.TimerCreated;
        cancellation.Cancel();
        await loop;

        passes.ShouldBe(1);
        clock.ActiveTimers.ShouldBe(0);
        logger.Entries.ShouldBeEmpty();
        heartbeat.LastSuccessfulPassAt.ShouldBe(Origin);
    }

    [Test]
    public async Task Heartbeat_is_healthy_through_the_boundary_and_recovers_after_a_successful_pass()
    {
        var harness = CreateHarness();
        var threshold = TimeSpan.FromSeconds(5 * 60);

        harness.Heartbeat.IsHealthy.ShouldBeTrue();
        harness.Clock.Advance(threshold);
        harness.Heartbeat.IsHealthy.ShouldBeTrue();
        harness.Clock.Advance(TimeSpan.FromTicks(1));
        harness.Heartbeat.IsHealthy.ShouldBeFalse();

        await harness.Runtime.RunPassAsync(_ => Task.FromResult(0), CancellationToken.None);
        harness.Heartbeat.IsHealthy.ShouldBeTrue();
        harness.Clock.Advance(threshold);
        harness.Heartbeat.IsHealthy.ShouldBeTrue();
        harness.Clock.Advance(TimeSpan.FromTicks(1));
        harness.Heartbeat.IsHealthy.ShouldBeFalse();
    }

    [Test]
    public void Runtime_options_have_safe_defaults_and_reject_nonpositive_or_inverted_ranges()
    {
        var defaults = new OutboxWorkerOptions();
        defaults.IdleInterval.ShouldBe(TimeSpan.FromSeconds(5));
        defaults.MaximumFailureBackoff.ShouldBe(TimeSpan.FromMinutes(1));
        defaults.UnhealthyAfterIntervals.ShouldBe(60);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OutboxWorker:IdleInterval"] = "00:00:00",
            ["OutboxWorker:MaximumFailureBackoff"] = "00:00:00",
            ["OutboxWorker:UnhealthyAfterIntervals"] = "0"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new ControlledTimeProvider(Origin));
        OutboxWorkerServiceCollectionExtensions.AddOutboxWorkerRuntime(services, configuration);
        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<OutboxWorkerOptions>>().Value);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Heartbeat_ready_check_is_registered_only_when_delivery_is_enabled(bool enabled)
    {
        var clock = new ControlledTimeProvider(Origin);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityAccess:Email:Enabled"] = enabled.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        OutboxWorkerServiceCollectionExtensions.AddOutboxWorkerRuntime(services, configuration);
        services.Count(descriptor => descriptor.ServiceType == typeof(OutboxWorkerHeartbeat)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        if (!enabled)
        {
            registrations.ShouldNotContain(registration => registration.Name == "outbox-worker-heartbeat");
            return;
        }

        var registration = registrations.Single(item => item.Name == "outbox-worker-heartbeat");
        registration.Tags.ShouldBe(["ready"], ignoreOrder: true);
        registration.FailureStatus.ShouldBe(HealthStatus.Unhealthy);
        var health = provider.GetRequiredService<HealthCheckService>();
        (await health.CheckHealthAsync(item => item.Name == registration.Name)).Status.ShouldBe(HealthStatus.Healthy);

        clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromTicks(1)));
        (await health.CheckHealthAsync(item => item.Name == registration.Name)).Status.ShouldBe(HealthStatus.Unhealthy);
        await provider.GetRequiredService<OutboxWorkerRuntime>()
            .RunPassAsync(_ => Task.FromResult(0), CancellationToken.None);
        (await health.CheckHealthAsync(item => item.Name == registration.Name)).Status.ShouldBe(HealthStatus.Healthy);
    }

    private static Harness CreateHarness()
    {
        var clock = new ControlledTimeProvider(Origin);
        var options = Options.Create(new OutboxWorkerOptions());
        var heartbeat = new OutboxWorkerHeartbeat(clock, options);
        var logger = new CapturingLogger<Worker>();
        return new Harness(clock, heartbeat, logger, new OutboxWorkerRuntime(clock, options, heartbeat, logger));
    }

    private static ActivityListener ListenToWorkerActivities(List<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == typeof(Worker).Assembly.GetName().Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed record Harness(
        ControlledTimeProvider Clock,
        OutboxWorkerHeartbeat Heartbeat,
        CapturingLogger<Worker> Logger,
        OutboxWorkerRuntime Runtime);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception));

        public sealed record Entry(LogLevel Level, string Text, Exception? Exception);
    }

    private sealed class TrackingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeTimers;

        public Task TimerCreated => _timerCreated.Task;
        public int ActiveTimers => Volatile.Read(ref _activeTimers);
        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _activeTimers);
            var timer = new TrackingTimer(TimeProvider.System.CreateTimer(callback, state, dueTime, period), this);
            _timerCreated.TrySetResult();
            return timer;
        }

        private void Released() => Interlocked.Decrement(ref _activeTimers);

        private sealed class TrackingTimer(ITimer inner, TrackingTimeProvider owner) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                inner.Dispose();
                owner.Released();
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                await inner.DisposeAsync();
                owner.Released();
            }
        }
    }
}
