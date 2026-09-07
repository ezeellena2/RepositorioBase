namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// How much of itself a deployment is willing to admit, after a restore (IA-REQ-055).
/// <para>
/// The order is deliberate: <see cref="Closed"/> is the default and the lowest, so a value nobody set, a value
/// nobody could parse, and a value that failed verification all land on the same answer.
/// </para>
/// </summary>
public enum RecoveryAdmissionState
{
    /// <summary>
    /// No public ingress, no session issued or accepted, no one-time token, no delivery. Only dataless liveness
    /// and readiness probes answer.
    /// </summary>
    Closed,

    /// <summary>
    /// Authentication and revalidation only. Sessions created before the epoch stamp count as revoked, and
    /// `OutboxSecret` rows predating it are refused and terminalized.
    /// </summary>
    Quarantined,

    /// <summary>Everything. It requires the evidence record's `release` claim and completed reconciliation.</summary>
    Open
}

/// <summary>Why admission is where it is, in the vocabulary C6 accepted for the audit.</summary>
public enum RecoveryAdmissionReason
{
    /// <summary>Nothing arms the guard: this deployment is not recovering from anything.</summary>
    NotRecovering,

    EvidenceMissing,
    EvidenceStale,
    EvidenceInvalid,
    Verified
}

/// <summary>
/// What admission is, and the moment before which nothing restored may be trusted.
/// <para>
/// <see cref="Epoch"/> is the stamp the evidence carries. Anything the restored database holds that predates it —
/// a session, a sealed envelope — is a thing from before the restore, and being from before the restore is the
/// whole reason not to honour it.
/// </para>
/// </summary>
public sealed record RecoveryAdmission(RecoveryAdmissionState State, RecoveryAdmissionReason Reason, DateTimeOffset? Epoch)
{
    /// <summary>The answer whenever there is any doubt at all.</summary>
    public static RecoveryAdmission ClosedBecause(RecoveryAdmissionReason reason) => new(RecoveryAdmissionState.Closed, reason, null);

    /// <summary>A deployment nobody armed. It is not recovering, so it admits what it always did.</summary>
    public static RecoveryAdmission NotRecovering { get; } = new(RecoveryAdmissionState.Open, RecoveryAdmissionReason.NotRecovering, null);

    public bool AdmitsPublicIngress => State != RecoveryAdmissionState.Closed;

    public bool AdmitsDelivery => State == RecoveryAdmissionState.Open;

    /// <summary>Whether something stamped at this moment is from before the restore, and therefore not to be honoured.</summary>
    public bool PredatesRecovery(DateTimeOffset stamp) => Epoch is { } epoch && stamp < epoch;
}

/// <summary>
/// The admission decision, taken from evidence held outside the restored database (IA-REQ-055).
/// <para>
/// It is evaluated on every process start rather than remembered, because "remembered" would mean remembered
/// somewhere — and the only place a restored deployment has to remember things is the restored database, which is
/// exactly what must not be able to open it.
/// </para>
/// </summary>
public interface IRecoveryAdmission
{
    RecoveryAdmission Current { get; }
}
