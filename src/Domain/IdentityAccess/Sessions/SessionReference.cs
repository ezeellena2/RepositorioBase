using System.Security.Cryptography;

namespace CleanArchitecture.Domain.IdentityAccess.Sessions;

/// <summary>
/// How a client addresses one of its own sessions. It is 128 bits of randomness, written once, and it is
/// deliberately not the <see cref="UserSessionId"/> the authentication cookie carries: a screen that listed the
/// ticket identifier would put the thing the cookie proves into a page anybody looking over a shoulder can read.
/// </summary>
public readonly record struct SessionReference
{
    private const int Bytes = 16;
    private const int EncodedLength = 22;

    private SessionReference(string value) => Value = value;

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static SessionReference New() => new(Encode(RandomNumberGenerator.GetBytes(Bytes)));

    /// <summary>Parses a reference a client sent back. A malformed one is not a reference, so it matches nothing.</summary>
    public static bool TryFrom(string? value, out SessionReference reference)
    {
        reference = default;
        if (value is null || value.Length != EncodedLength) return false;
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')) return false;
        reference = new SessionReference(value);
        return true;
    }

    /// <summary>The persistence entry point. It trusts the column, which only <see cref="New"/> ever wrote.</summary>
    internal static SessionReference FromPersistedValue(string value) => new(value);

    public override string ToString() => Value;

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
