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

    /// <summary>
    /// Rehydrates a value this type previously produced. It belongs to persistence, not to callers holding a
    /// token: anything reaching it has already been through <see cref="Of"/> once.
    /// </summary>
    public static VersionedTokenHash FromPersistedValue(string value)
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

    public override string ToString() => Value;

    private static bool IsWellFormed(string value)
    {
        if (!value.StartsWith(Version, StringComparison.Ordinal))
        {
            return false;
        }

        var digest = value.AsSpan(Version.Length);
        if (digest.Length != DigestLength || digest[^1] != '=')
        {
            return false;
        }

        foreach (var character in digest[..^1])
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '+' && character != '/')
            {
                return false;
            }
        }

        return true;
    }
}
