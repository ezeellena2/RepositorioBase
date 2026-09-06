namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// One retained lookup row for a person's documentary identity. It exists so a document can be found again without
/// anything being able to read it back: the value is a keyed digest, not a reversible encoding.
/// </summary>
public sealed class IdentityDocumentFingerprint
{
    private IdentityDocumentFingerprint()
    {
    }

    public Guid IdentityId { get; private set; }

    public int KeyVersion { get; private set; }

    public string Fingerprint { get; private set; } = string.Empty;

    internal static IdentityDocumentFingerprint Create(Guid identityId, int keyVersion, string fingerprint) =>
        new()
        {
            IdentityId = identityId,
            KeyVersion = keyVersion,
            Fingerprint = fingerprint
        };
}
