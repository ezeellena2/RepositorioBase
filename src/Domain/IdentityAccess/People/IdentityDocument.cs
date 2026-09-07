using System.Text.RegularExpressions;

namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// A person's protected documentary identity: the ciphertext of the canonical tuple, and one keyed fingerprint per
/// retained key version. No member of this aggregate holds the number, and nothing here can produce it — reading it
/// back needs the protector, which lives outside the Domain (IA-REQ-050).
/// </summary>
public sealed partial class IdentityDocument
{
    private readonly List<IdentityDocumentFingerprint> _fingerprints = new();

    private IdentityDocument()
    {
    }

    public Guid IdentityId { get; private set; }

    public IdentityDocumentCountry Country { get; private set; }

    public IdentityDocumentKind DocumentType { get; private set; }

    public string Ciphertext { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public DataClassification Classification { get; private set; }

    public IReadOnlyCollection<IdentityDocumentFingerprint> Fingerprints => _fingerprints.AsReadOnly();

    public static IdentityDocument Record(
        Guid identityId,
        IdentityDocumentCountry country,
        IdentityDocumentKind documentType,
        string ciphertext,
        IReadOnlyList<DocumentFingerprintValue> fingerprints,
        DataClassification classification,
        DateTimeOffset now)
    {
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        if (!Enum.IsDefined(country)) throw new ArgumentOutOfRangeException(nameof(country));
        if (!Enum.IsDefined(documentType)) throw new ArgumentOutOfRangeException(nameof(documentType));
        if (!Enum.IsDefined(classification)) throw new ArgumentOutOfRangeException(nameof(classification));
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        ArgumentNullException.ThrowIfNull(fingerprints);

        // A document with no fingerprint would be unfindable and, worse, would not occupy the unique index — so the
        // same number could be recorded again by somebody else while this row still holds it.
        if (fingerprints.Count == 0)
        {
            throw new ArgumentException("A recorded document must carry at least one fingerprint.", nameof(fingerprints));
        }

        foreach (var fingerprint in fingerprints)
        {
            EnsureFingerprint(fingerprint);
        }

        var document = new IdentityDocument
        {
            IdentityId = identityId,
            Country = country,
            DocumentType = documentType,
            Ciphertext = ciphertext,
            RecordedAt = now,
            Classification = classification
        };

        foreach (var fingerprint in fingerprints)
        {
            document._fingerprints.Add(IdentityDocumentFingerprint.Create(identityId, fingerprint.KeyVersion, fingerprint.Value));
        }

        return document;
    }

    /// <summary>
    /// Erases both halves at once. Leaving the fingerprints would keep the number occupying the unique index after
    /// its ciphertext is gone, which is the opposite of reclaimable (IA-REQ-056).
    /// </summary>
    public void Purge(DateTimeOffset now, string? policyId = null, string? policyVersion = null)
    {
        if (PurgedAt is not null)
        {
            throw new InvalidOperationException("A purged document cannot be purged again.");
        }

        Ciphertext = string.Empty;
        _fingerprints.Clear();
        PurgedAt = now;

        // The tombstone says which policy authorized this, so the row that is left is not merely empty but
        // accounted for. Both halves or neither: a version without an identifier names nothing (IA-REQ-056).
        if (!string.IsNullOrWhiteSpace(policyId) && !string.IsNullOrWhiteSpace(policyVersion))
        {
            PurgePolicyId = policyId;
            PurgePolicyVersion = policyVersion;
        }
    }

    /// <summary>Which policy authorized the purge, on the non-identifying tombstone it left behind.</summary>
    public string? PurgePolicyId { get; private set; }

    public string? PurgePolicyVersion { get; private set; }

    /// <summary>
    /// The shape a keyed fingerprint must have to be storable: its own key version, the digest version, and a
    /// canonical Base64 digest. Anything else is protection material this aggregate refuses to trust.
    /// </summary>
    private static void EnsureFingerprint(DocumentFingerprintValue fingerprint)
    {
        if (fingerprint.KeyVersion < 1)
        {
            throw new ArgumentException("Fingerprint key versions start at one.", nameof(fingerprint));
        }

        if (string.IsNullOrWhiteSpace(fingerprint.Value) || !FingerprintFormat().IsMatch(fingerprint.Value))
        {
            throw new ArgumentException("A fingerprint must be a versioned, keyed digest.", nameof(fingerprint));
        }

        if (!fingerprint.Value.StartsWith($"k{fingerprint.KeyVersion}:", StringComparison.Ordinal))
        {
            throw new ArgumentException("A fingerprint must name the key version it was produced with.", nameof(fingerprint));
        }
    }

    [GeneratedRegex(@"^k[1-9][0-9]*:v1:[A-Za-z0-9+/]{43}=$")]
    private static partial Regex FingerprintFormat();
}
