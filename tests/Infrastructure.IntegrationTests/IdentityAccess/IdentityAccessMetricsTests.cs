using System.Diagnostics.Metrics;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IdentityAccess.Observability;
using CleanArchitecture.Infrastructure.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What the deployment is allowed to say about itself (IA-REQ-029, IA-REQ-032).
/// <para>
/// A metric label is retained far longer than a log line and is indexed for querying, so a label carrying a key,
/// an address or an identity would be a searchable directory of who tried what — built, of all things, by the
/// instrument meant to be watching for abuse. These tests read the measurements as an exporter would and hold the
/// label set to what an operator actually needs: which budget, and what it decided.
/// </para>
/// </summary>
public sealed class IdentityAccessMetricsTests
{
    private static readonly DateTimeOffset Now = new(2031, 7, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly List<string> _scopes = [];

    [TearDown]
    public async Task Remove_only_this_tests_budgets()
    {
        if (_scopes.Count == 0) return;
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.IdentityAttemptBudgets.Where(budget => _scopes.Contains(budget.Scope)).ExecuteDeleteAsync();
        _scopes.Clear();
    }

    [Test]
    public async Task A_budget_decision_is_counted_by_scope_and_outcome_and_by_nothing_else()
    {
        using var scope = TestServices.CreateScope();
        var budget = Budget(1);
        using var metrics = TestMetrics.Create();
        var adapter = new PostgreSqlAttemptBudget(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), new FixedTime(Now), metrics);
        const string key = "jane.doe@example.test";
        using var recorded = new Recorder("identity_access.attempt_budget.decisions");

        await adapter.SpendAsync(budget, key, CancellationToken.None);
        await adapter.SpendAsync(budget, key, CancellationToken.None);

        var measurements = recorded.Measurements;
        measurements.Count.ShouldBe(2, "an admitted attempt is as much of a signal as a refused one");
        measurements.Select(measurement => measurement.Tags["outcome"]).ShouldBe(["Admitted", "Exhausted"]);
        foreach (var measurement in measurements)
        {
            measurement.Tags.Keys.Order(StringComparer.Ordinal).ShouldBe(["outcome", "scope"]);
            measurement.Tags["scope"].ShouldBe(budget.Scope);
            measurement.Tags.Values.Any(value => value.Contains("jane.doe", StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse("the key is what a budget bounds, never what it publishes");
        }
    }

    /// <summary>
    /// The digest is not a safe label either. It is stable per caller, so an exporter holding it can count one
    /// person's attempts across days — which is the directory this instrument must not become.
    /// </summary>
    [Test]
    public async Task The_stored_digest_of_a_key_is_not_published_as_a_label_either()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var budget = Budget(3);
        using var metrics = TestMetrics.Create();
        using var recorded = new Recorder("identity_access.attempt_budget.decisions");

        await new PostgreSqlAttemptBudget(context, new FixedTime(Now), metrics)
            .SpendAsync(budget, "someone@example.test", CancellationToken.None);

        var digest = (await context.IdentityAttemptBudgets.SingleAsync(row => row.Scope == budget.Scope)).KeyHash;
        recorded.Measurements
            .SelectMany(measurement => measurement.Tags.Values)
            .ShouldNotContain(digest);
    }

    private AttemptBudget Budget(int limit)
    {
        var scope = $"test.metrics.{Guid.NewGuid():N}";
        _scopes.Add(scope);
        return new AttemptBudget(scope, limit, TimeSpan.FromMinutes(15));
    }

    /// <summary>Reads the meter the way an exporter does, rather than the way the class that writes it does.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Measured> _measurements = [];

        internal Recorder(string instrument)
        {
            _listener.InstrumentPublished = (published, listener) =>
            {
                if (published.Meter.Name == IdentityAccessMetrics.MeterName && published.Name == instrument)
                {
                    listener.EnableMeasurementEvents(published);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                lock (_measurements)
                {
                    _measurements.Add(new Measured(value, tags.ToArray().ToDictionary(
                        tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal)));
                }
            });
            _listener.Start();
        }

        internal IReadOnlyList<Measured> Measurements
        {
            get
            {
                lock (_measurements) return [.. _measurements];
            }
        }

        public void Dispose() => _listener.Dispose();

        internal sealed record Measured(long Value, IReadOnlyDictionary<string, string> Tags);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
