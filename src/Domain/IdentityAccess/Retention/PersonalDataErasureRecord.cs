namespace CleanArchitecture.Domain.IdentityAccess.Retention;

/// <summary>
/// Evidence that an erasure happened, written in the same transaction as the erasure itself (IA-REQ-056).
/// <para>
/// It is deliberately not an audit event. `AuditEvent` admits only `reason`, `code` and `outcome`, and what has
/// to survive here is which policy authorized the deletion, at which version, and how many rows it reached — so
/// the evidence is its own append-only record rather than metadata squeezed into a shape that cannot hold it.
/// </para>
/// <para>
/// It names the subject, and nothing about them. A record of a deletion that itself contained the deleted thing
/// would be the deletion not having happened.
/// </para>
/// </summary>
public sealed class PersonalDataErasureRecord
{
    private PersonalDataErasureRecord() { }

    public Guid RecordId { get; private set; }

    public Guid SubjectIdentityId { get; private set; }

    /// <summary>The policy category this erasure was performed under, as its name.</summary>
    public string Category { get; private set; } = string.Empty;

    public string PolicyId { get; private set; } = string.Empty;

    public string PolicyVersion { get; private set; } = string.Empty;

    public DateTimeOffset ExecutedAt { get; private set; }

    public int AffectedRowCount { get; private set; }

    public static PersonalDataErasureRecord Of(
        Guid subjectIdentityId, string category, string policyId, string policyVersion, int affectedRowCount, DateTimeOffset now)
    {
        if (subjectIdentityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(subjectIdentityId));
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);

        // Zero would be a record of nothing having happened, which is not evidence of an erasure.
        if (affectedRowCount <= 0) throw new ArgumentOutOfRangeException(nameof(affectedRowCount));

        return new PersonalDataErasureRecord
        {
            RecordId = Guid.NewGuid(),
            SubjectIdentityId = subjectIdentityId,
            Category = category,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            ExecutedAt = now,
            AffectedRowCount = affectedRowCount
        };
    }
}
