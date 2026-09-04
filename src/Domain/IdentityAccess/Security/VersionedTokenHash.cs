using System.Security.Cryptography;
using System.Text;

namespace CleanArchitecture.Domain.IdentityAccess.Security;

/// <summary>
/// The hash of a token, separated from the token by type rather than by shape.
/// <para>
/// Shape alone cannot tell them apart: a generated token is 32 random bytes in Base64 and a SHA-256 digest is 32
/// bytes in Base64, so any check that inspects a string would accept a token someone prefixed with the version
/// tag. This type removes the question by refusing to accept a precomputed value at all — <see cref="Of"/> takes
/// the token and hashes it, so passing the token where the hash belongs now stores the hash. The one parsing
/// entry point is named for persistence and exists so a stored value can be rehydrated.
/// </para>
/// </summary>
public readonly record struct VersionedTokenHash
{
    /// <summary>The one format this project emits. A new version changes this, the database constraint and a migration together.</summary>
    private const string Version = "v1:";

    /// <summary>Base64 of a 32-byte digest is 44 characters, the last of which is always padding.</summary>
    private const int DigestLength = 44;

    private VersionedTokenHash(string value) => Value = value;

    public string Value { get; }

    /// <summary>Hashes a token. This is the only way to obtain a hash of a token that is not already stored.</summary>
    public static VersionedTokenHash Of(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new VersionedTokenHash(Version + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
    }

    /// <summary>True for the uninitialized value, which no aggregate may ever store.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Rehydrates a value this type previously produced. It is internal because it is the one entry that accepts
    /// a precomputed string: leaving it public would put back exactly the door <see cref="Of"/> exists to close,
    /// since a caller holding a token could tag it and present it as a hash. Only persistence needs it.
    /// </summary>
    internal static VersionedTokenHash FromPersistedValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsWellFormed(value))
        {
            throw new ArgumentException("A persisted token hash must carry its version tag and a digest.", nameof(value));
        }

        return new VersionedTokenHash(value);
    }

    /// <summary>Compares in fixed time, so a mismatch reveals nothing about how much of the digest matched.</summary>
    public bool Matches(string token) =>
        !string.IsNullOrWhiteSpace(token) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Of(token).Value),
            Encoding.UTF8.GetBytes(Value));

    /// <summary>
    /// Whether a stored value is one this type could have produced. It answers about a string without
    /// returning a hash, so persistence and its tests can check the rule without a way to bypass it.
    /// </summary>
    public static bool IsPersistable(string? value) => !string.IsNullOrWhiteSpace(value) && IsWellFormed(value);

    public override string ToString() => Value;

    private static bool IsWellFormed(string value)
    {
        if (!value.StartsWith(Version, StringComparison.Ordinal))
        {
            return false;
        }

        var digest = value[Version.Length..];
        if (digest.Length != DigestLength || digest[^1] != '=')
        {
            return false;
        }

        // Decode and re-encode. A length-and-charset check still admits a non-canonical encoding — one whose
        // final character carries bits the 32-byte payload does not use — and several distinct strings would then
        // name the same digest, so the unique index would stop meaning one token per row.
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(digest, bytes, out var written) &&
            written == bytes.Length &&
            string.Equals(digest, Convert.ToBase64String(bytes), StringComparison.Ordinal);
    }
}
