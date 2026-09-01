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
}

public interface IOutboxSecretWriter
{
    string Encrypt(string token);
}

public interface IRegistrationInitialRoleProvisioner
{
    void AssignResponsibleOwner(Tenant tenant, TenantMembership membership);
}
