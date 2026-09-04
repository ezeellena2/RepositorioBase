namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

public interface ISecureTokenGenerator
{
    string Generate();
}

public interface ITokenHasher
{
    string Hash(string token);

    bool Verify(string token, string versionedHash);

    /// <summary>
    /// The typed hash of a token. Callers take this rather than parsing <see cref="Hash"/> back into a
    /// <c>VersionedTokenHash</c>, so no application code ever holds an entry that accepts a precomputed value.
    /// </summary>
    CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash Of(string token);
}

public interface IOutboxSecretWriter
{
    string Encrypt(string token);
}

public interface IRegistrationInitialRoleProvisioner
{
    void AssignResponsibleOwner(Tenant tenant, TenantMembership membership);
}
