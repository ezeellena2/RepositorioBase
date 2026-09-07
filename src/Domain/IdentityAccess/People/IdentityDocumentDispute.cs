using System.Text.RegularExpressions;

namespace CleanArchitecture.Domain.IdentityAccess.People;

public enum DocumentDisputeStatus
{
    Open,
    Corrected,
    Rejected
}

/// <summary>
/// One person's claim that the document recorded against them is wrong (IA-REQ-058).
/// <para>
/// It carries the claimed tuple as ciphertext and nothing else. The number reaches this aggregate already
/// protected, exactly as the recorded one does — so a dispute is safe to store, safe to leave open, and safe to
/// read back by anything that is not the resolver.
/// </para>
/// <para>
/// Opening one changes nothing. The account keeps signing in, the `Personal` tenant keeps working, and the
/// recorded document keeps its value until a resolution commits: a dispute is a request for a second party, not
/// an edit waiting to be applied.
/// </para>
/// </summary>
public sealed partial class IdentityDocumentDispute
{
    private IdentityDocumentDispute() { }

    public Guid DisputeId { get; private set; }

    public Guid SubjectIdentityId { get; private set; }

    /// <summary>Why, as a short code the person chose from the screen. Never free text.</summary>
    public string ReasonCode { get; private set; } = string.Empty;

    /// <summary>The claimed tuple, protected. No member of this aggregate can produce the number.</summary>
    public string ClaimedCiphertext { get; private set; } = string.Empty;

    public DocumentDisputeStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public int Version { get; private set; }

    public bool IsOpen => Status == DocumentDisputeStatus.Open;

    public static IdentityDocumentDispute Open(Guid subjectIdentityId, string reasonCode, string claimedCiphertext, DateTimeOffset now)
    {
        if (subjectIdentityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(subjectIdentityId));
        if (!IsAcceptedCode(reasonCode)) throw new ArgumentException("A reason code is a short stable identifier.", nameof(reasonCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedCiphertext);

        return new IdentityDocumentDispute
        {
            DisputeId = Guid.NewGuid(),
            SubjectIdentityId = subjectIdentityId,
            ReasonCode = reasonCode,
            ClaimedCiphertext = claimedCiphertext,
            Status = DocumentDisputeStatus.Open,
            OpenedAt = now,
            Version = 1
        };
    }

    public void Settle(DocumentDisputeStatus outcome, DateTimeOffset now)
    {
        if (outcome is not (DocumentDisputeStatus.Corrected or DocumentDisputeStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (Status != DocumentDisputeStatus.Open) throw new InvalidOperationException("A settled dispute cannot be settled again.");
        if (now < OpenedAt) throw new ArgumentOutOfRangeException(nameof(now));

        Status = outcome;
        ResolvedAt = now;
        Version++;
    }

    /// <summary>The shape a reason code and an evidence reference share. The 64 is a **product default**.</summary>
    public static bool IsAcceptedCode(string? value) => value is not null && CodeFormat().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex CodeFormat();
}

/// <summary>
/// Evidence that a document was corrected, written in the same transaction as the correction (IA-REQ-058).
/// <para>
/// It holds no document value — not the old one, not the new one, not a fingerprint. What it holds is who
/// resolved it, against which external case, and which key versions the replaced fingerprints were written under,
/// which is what makes a later key rotation auditable without making any of this readable.
/// </para>
/// </summary>
public sealed class IdentityDocumentCorrectionRecord
{
    private IdentityDocumentCorrectionRecord() { }

    public Guid RecordId { get; private set; }

    public Guid SubjectIdentityId { get; private set; }

    public Guid DisputeId { get; private set; }

    public DateTimeOffset ResolvedAt { get; private set; }

    public Guid ResolvedByMembershipId { get; private set; }

    public string EvidenceReference { get; private set; } = string.Empty;

    /// <summary>The key versions the replaced fingerprints carried, as a sorted comma-separated list.</summary>
    public string PreviousKeyVersions { get; private set; } = string.Empty;

    public static IdentityDocumentCorrectionRecord Of(
        Guid subjectIdentityId,
        Guid disputeId,
        Guid resolvedByMembershipId,
        string evidenceReference,
        IEnumerable<int> previousKeyVersions,
        DateTimeOffset now)
    {
        if (subjectIdentityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(subjectIdentityId));
        if (disputeId == Guid.Empty) throw new ArgumentException("A correction names the dispute it settled.", nameof(disputeId));
        if (resolvedByMembershipId == Guid.Empty) throw new ArgumentException("A correction names who resolved it.", nameof(resolvedByMembershipId));
        if (!IdentityDocumentDispute.IsAcceptedCode(evidenceReference))
            throw new ArgumentException("An evidence reference is a short stable identifier.", nameof(evidenceReference));

        return new IdentityDocumentCorrectionRecord
        {
            RecordId = Guid.NewGuid(),
            SubjectIdentityId = subjectIdentityId,
            DisputeId = disputeId,
            ResolvedAt = now,
            ResolvedByMembershipId = resolvedByMembershipId,
            EvidenceReference = evidenceReference,
            PreviousKeyVersions = string.Join(',', previousKeyVersions.Distinct().Order())
        };
    }
}
