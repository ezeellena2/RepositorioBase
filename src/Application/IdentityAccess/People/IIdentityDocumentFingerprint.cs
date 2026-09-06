using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// Produces the keyed, versioned lookup values that make a documentary identity unique without making it readable.
/// <para>
/// It answers with one value per retained key version rather than one for the current key, because uniqueness has to
/// keep holding while a key is being rotated: a row written under the old key and a claim made under the new one
/// must still collide.
/// </para>
/// </summary>
public interface IIdentityDocumentFingerprint
{
    /// <summary>The version new rows are written under. Retained older versions are still produced for lookups.</summary>
    int CurrentKeyVersion { get; }

    IReadOnlyList<DocumentFingerprintValue> ForRetainedKeys(NormalizedDocument document);
}
