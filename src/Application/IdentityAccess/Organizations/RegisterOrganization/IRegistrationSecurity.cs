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
    /// <summary>
    /// Creates the system `Owner` role, grants it the codes the catalogue says an `Organization` owner holds, and
    /// assigns it to the responsible membership. The grant is not decoration: C5's ceiling lets an actor grant
    /// only what it effectively holds, so an owner provisioned with nothing could never begin (amendment D1).
    /// </summary>
    Task AssignResponsibleOwnerAsync(Tenant tenant, TenantMembership membership, CancellationToken cancellationToken);
}
