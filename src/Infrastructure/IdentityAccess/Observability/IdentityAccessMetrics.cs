using System.Diagnostics.Metrics;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Observability;

/// <summary>
/// What an operator is told about identity access, and nothing else (IA-REQ-029, IA-REQ-032).
/// <para>
/// Every instrument here is a count with a closed set of labels: a budget scope, a decision, a settlement reason,
/// a retention category, an admission state. There is deliberately no label carrying a key, an address, an
/// identity, a session or a message — a metric label is retained far longer than a log line and is indexed, so a
/// per-caller label is a directory of who did what, built by the thing meant to be watching for abuse.
/// </para>
/// <para>
/// That is also what makes these safe to alert on. "Refusals for `identity.login.account` rose" and "settlements
/// with reason `envelope_unreadable` began" are the two shapes of question an operator actually has, and neither
/// needs to know who.
/// </para>
/// </summary>
public sealed class IdentityAccessMetrics : IDisposable
{
    /// <summary>Also named in ServiceDefaults, which is where the meter is subscribed for export.</summary>
    public const string MeterName = "CleanArchitecture.IdentityAccess";

    private readonly Meter _meter;
    private readonly Counter<long> _budgetDecisions;
    private readonly Counter<long> _outboxSettlements;
    private readonly Counter<long> _retentionErasures;

    public IdentityAccessMetrics(IMeterFactory factory, IRecoveryAdmission admission)
    {
        _meter = factory.Create(MeterName);
        _budgetDecisions = _meter.CreateCounter<long>(
            "identity_access.attempt_budget.decisions",
            description: "Attempt-budget decisions, by budget scope and outcome.");
        _outboxSettlements = _meter.CreateCounter<long>(
            "identity_access.outbox.settlements",
            description: "Outbox messages settled, by status and reason code.");
        _retentionErasures = _meter.CreateCounter<long>(
            "identity_access.retention.erased",
            description: "Rows erased by retention maintenance, by category.");

        // The one thing an operator needs to know before anything else about a restored deployment, and the one
        // they cannot see from the outside without making a request that the state itself would refuse.
        _meter.CreateObservableGauge(
            "identity_access.recovery.admission",
            () => new Measurement<int>(1, new KeyValuePair<string, object?>("state", admission.Current.State.ToString())),
            description: "1 for the admission state this process decided at start.");
    }

    public void Record(AttemptBudget budget, AttemptBudgetOutcome outcome) =>
        _budgetDecisions.Add(1, new KeyValuePair<string, object?>("scope", budget.Scope), new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    /// <summary>
    /// <paramref name="reason"/> comes from the dispatcher's closed set of settlement codes. It is never a
    /// provider message: those are unbounded and are the one place a recipient address has been seen to appear.
    /// </summary>
    public void RecordSettlement(string status, string? reason) =>
        _outboxSettlements.Add(1, new KeyValuePair<string, object?>("status", status), new KeyValuePair<string, object?>("reason", reason ?? "none"));

    public void RecordErasure(string category, int rows)
    {
        if (rows > 0) _retentionErasures.Add(rows, new KeyValuePair<string, object?>("category", category));
    }

    public void Dispose() => _meter.Dispose();
}
