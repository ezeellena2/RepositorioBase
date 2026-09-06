namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// One keyed, versioned lookup value for a documentary identity. There is one per retained fingerprint key, so a
/// rotation can hold the old and the new value at once and uniqueness keeps holding across both.
/// </summary>
public readonly record struct DocumentFingerprintValue(int KeyVersion, string Value);
