using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class VersionedTokenHasher : ITokenHasher
{
    private const string Version = "v1:";

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Version + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public bool Verify(string token, string versionedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(versionedHash) || !versionedHash.StartsWith(Version, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Hash(token));
        var actual = Encoding.UTF8.GetBytes(versionedHash);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
