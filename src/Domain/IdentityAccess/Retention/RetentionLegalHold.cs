using System.Text.RegularExpressions;

namespace CleanArchitecture.Domain.IdentityAccess.Retention;

/// <summary>
/// One operator's decision that a subject's data must not be erased yet (IA-REQ-056, amendment A4).
/// <para>
/// Its whole effect is that: a held subject is skipped by every purge and therefore never reaches C6's `Closed`,
/// which only an executed erasure produces. It is not an account state, it suspends nobody, it refuses no sign-in,
/// it blocks no reactivation, and no authorization decision reads it. Everything this record can do is stop a
/// deletion — which is why it lives here rather than anywhere near the lifecycle.
/// </para>
/// <para>
/// It records who placed it, as a membership rather than an identity: the authority came from operating Platform,
/// and a membership is the thing that says so at a point in time.
/// </para>
/// </summary>
public sealed partial class RetentionLegalHold
{
    private RetentionLegalHold() { }

    public Guid HoldId { get; private set; }

    public Guid SubjectIdentityId { get; private set; }

    /// <summary>Why, as an operator's own code. Two reasons are two decisions and each ends separately.</summary>
    public string ReasonCode { get; private set; } = string.Empty;

    /// <summary>
    /// The operator's own case number. Constrained rather than free text because a retention record is not a
    /// place to write prose about a person, and this table is read by people who are not the subject.
    /// </summary>
    public string Reference { get; private set; } = string.Empty;

    public DateTimeOffset PlacedAt { get; private set; }

    public Guid PlacedByMembershipId { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public int Version { get; private set; }

    public bool IsActive => ReleasedAt is null;

    public static RetentionLegalHold Place(
        Guid subjectIdentityId, string reasonCode, string reference, Guid placedByMembershipId, DateTimeOffset now)
    {
        if (subjectIdentityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(subjectIdentityId));
        if (placedByMembershipId == Guid.Empty) throw new ArgumentException("A hold records the membership that placed it.", nameof(placedByMembershipId));
        if (!IsAcceptedReference(reasonCode)) throw new ArgumentException("A reason code is a short stable identifier.", nameof(reasonCode));
        if (!IsAcceptedReference(reference)) throw new ArgumentException("A reference is a short stable identifier.", nameof(reference));

        return new RetentionLegalHold
        {
            HoldId = Guid.NewGuid(),
            SubjectIdentityId = subjectIdentityId,
            ReasonCode = reasonCode,
            Reference = reference,
            PlacedAt = now,
            PlacedByMembershipId = placedByMembershipId,
            Version = 1
        };
    }

    /// <summary>
    /// Reports whether this call is the one that released it. A second release changes nothing and says so, which
    /// is what lets the route be idempotent without pretending the first release did not happen.
    /// </summary>
    public bool Release(DateTimeOffset now)
    {
        if (ReleasedAt is not null) return false;
        if (now < PlacedAt) throw new ArgumentOutOfRangeException(nameof(now));
        ReleasedAt = now;
        Version++;
        return true;
    }

    /// <summary>The shape both the reason and the reference must have. The 64 is a **product default**.</summary>
    public static bool IsAcceptedReference(string? value) =>
        value is not null && ReferenceFormat().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex ReferenceFormat();
}
