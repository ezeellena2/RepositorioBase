using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

/// <summary>
/// Turns a documentary identity into the keyed digests the database indexes. The digest is the only thing that makes
/// a document findable, and it is one-way: holding every fingerprint row reveals no number.
/// </summary>
public sealed class IdentityDocumentFingerprintFactory(IOptions<IdentityDocumentProtectionOptions> options) : IIdentityDocumentFingerprint
{
    private const int MinimumKeyBytes = 32;

    public int CurrentKeyVersion => options.Value.CurrentKeyVersion;

    public IReadOnlyList<DocumentFingerprintValue> ForRetainedKeys(NormalizedDocument document)
    {
        var keys = Validated();
        var canonical = Encoding.UTF8.GetBytes(document.Canonical);

        // The current version leads, so the caller writes its new row under it without having to know which is which;
        // the retained versions follow so a lookup made during a rotation still collides with rows written before it.
        return keys
            .OrderByDescending(pair => pair.Key == CurrentKeyVersion)
            .ThenByDescending(pair => pair.Key)
            .Select(pair => new DocumentFingerprintValue(
                pair.Key,
                $"k{pair.Key}:v1:{Convert.ToBase64String(HMACSHA256.HashData(pair.Value, canonical))}"))
            .ToList();
    }

    private Dictionary<int, byte[]> Validated()
    {
        var configured = options.Value;
        if (configured.CurrentKeyVersion < 1)
        {
            throw new InvalidOperationException("Identity document fingerprint key versions start at one.");
        }

        if (configured.FingerprintKeys.Count == 0)
        {
            throw new InvalidOperationException("Identity document fingerprint key material is not configured.");
        }

        var keys = new Dictionary<int, byte[]>(configured.FingerprintKeys.Count);
        foreach (var (version, material) in configured.FingerprintKeys)
        {
            if (version < 1)
            {
                throw new InvalidOperationException("Identity document fingerprint key versions start at one.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(material ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Identity document fingerprint key {version} is not Base64 key material.");
            }

            if (key.Length < MinimumKeyBytes)
            {
                throw new InvalidOperationException($"Identity document fingerprint key {version} is shorter than {MinimumKeyBytes} bytes.");
            }

            keys[version] = key;
        }

        if (!keys.ContainsKey(configured.CurrentKeyVersion))
        {
            throw new InvalidOperationException($"Identity document fingerprint key {configured.CurrentKeyVersion} is configured as current but has no key material.");
        }

        return keys;
    }
}
