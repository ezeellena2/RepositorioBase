using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Application.IdentityAccess.Platform;
using Microsoft.AspNetCore.DataProtection;

namespace CleanArchitecture.Infrastructure.Security;

/// <summary>
/// The Platform second factor: RFC 6238 TOTP over a secret that is only ever stored encrypted (IA-REQ-041).
/// <para>
/// The secret is protected with the same Data Protection key ring the outbox envelopes use, so a deployment that
/// can read one can read the other and there is a single key rotation story rather than two.
/// </para>
/// <para>
/// Verification accepts the step before and after the current one. Clocks drift, and a factor that refused a code
/// a correct authenticator had just produced would be a factor people work around; one step either side is the
/// conventional tolerance and costs three candidate windows rather than one.
/// </para>
/// </summary>
public sealed class PlatformTotpSecretProtector(IDataProtectionProvider dataProtectionProvider) : IPlatformMfaVerifier
{
    private const int SecretBytes = 20;
    private const int Digits = 6;
    private const string Issuer = "CleanArchitecture Platform";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("identity-access.platform.totp-secret.v1");

    public PlatformMfaSecret Create(string accountName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        var sharedKey = Base32Encode(secret);
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(accountName)}" +
                  $"?secret={sharedKey}&issuer={Uri.EscapeDataString(Issuer)}&digits={Digits}&period={(int)Step.TotalSeconds}";
        return new PlatformMfaSecret(_protector.Protect(sharedKey), sharedKey, uri);
    }

    public bool Verify(string encryptedSecret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string sharedKey;
        try
        {
            sharedKey = _protector.Unprotect(encryptedSecret);
        }
        catch (CryptographicException)
        {
            // A secret this deployment cannot read is one nobody can prove; it is never treated as a pass.
            return false;
        }

        byte[] secret;
        try
        {
            secret = Base32Decode(sharedKey);
        }
        catch (FormatException)
        {
            return false;
        }

        var submitted = code.Trim();
        var counter = now.ToUnixTimeSeconds() / (long)Step.TotalSeconds;
        var matched = false;
        for (var drift = -1; drift <= 1; drift++)
        {
            // Every window is evaluated even once one has matched, so the answer takes the same time whichever
            // window a valid code came from.
            matched |= CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Compute(secret, counter + drift)),
                Encoding.ASCII.GetBytes(submitted.PadRight(Digits).AsSpan(0, Digits).ToString()));
        }

        return matched && submitted.Length == Digits;
    }

    private static string Compute(byte[] secret, long counter)
    {
        Span<byte> buffer = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer, counter);
        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(secret, buffer, mac);

        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return builder.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var bytes = new List<byte>(value.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var character in value.TrimEnd('='))
        {
            var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(character));
            if (index < 0) throw new FormatException("The shared key is not Base32.");
            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }
}

/// <summary>
/// Mints one-time recovery codes and hashes them, so the database holds no usable code (IA-REQ-041).
/// <para>
/// The hash is a plain SHA-256 rather than a password hash, and deliberately so: a recovery code is 100 bits of
/// machine-generated entropy, not a memorable secret, so there is nothing for work factors to defend against —
/// and a lookup by hash has to be fast enough to stay constant-time in practice.
/// </para>
/// </summary>
public sealed class PlatformRecoveryCodeHasher : IPlatformRecoveryCodeFactory
{
    private const int CodeCount = 10;
    private const int CodeBytes = 13;

    public IReadOnlyList<PlatformRecoveryCodeIssue> Create()
    {
        var issued = new List<PlatformRecoveryCodeIssue>(CodeCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (issued.Count < CodeCount)
        {
            var code = Format(RandomNumberGenerator.GetBytes(CodeBytes));
            if (!seen.Add(code)) continue;
            issued.Add(new PlatformRecoveryCodeIssue(code, Hash(code)));
        }

        return issued;
    }

    public string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return "v1:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
    }

    /// <summary>Grouped for transcription, and normalized on the way in so the groups are cosmetic.</summary>
    private static string Format(ReadOnlySpan<byte> entropy)
    {
        var raw = Convert.ToHexStringLower(entropy)[..20];
        return $"{raw[..5]}-{raw[5..10]}-{raw[10..15]}-{raw[15..]}";
    }

    private static string Normalize(string code) =>
        code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
