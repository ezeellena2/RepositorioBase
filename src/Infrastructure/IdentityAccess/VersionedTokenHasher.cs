using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Security;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Adapts the token-hash value object to the Application port. The format and the hashing live in one place, so
/// a caller reaching the port and a caller holding the value object can never disagree about what a hash is.
/// </summary>
public sealed class VersionedTokenHasher : ITokenHasher
{
    public string Hash(string token) => VersionedTokenHash.Of(token).Value;

    public bool Verify(string token, string versionedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(versionedHash))
        {
            return false;
        }

        try
        {
            return VersionedTokenHash.FromPersistedValue(versionedHash).Matches(token);
        }
        catch (ArgumentException)
        {
            // A stored value that is not a hash matches nothing; it is never an unexpected failure.
            return false;
        }
    }
}
