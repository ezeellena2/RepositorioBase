using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// The row that makes "one Personal context per identity" a database fact rather than an application precheck. It is
/// keyed by the identity and unique on the tenant, so neither direction can be doubled by a race.
/// </summary>
public sealed class PersonalTenantOwnership
{
    private PersonalTenantOwnership()
    {
    }

    public Guid IdentityId { get; private set; }

    public TenantId TenantId { get; private set; }

    public static PersonalTenantOwnership Create(Tenant personalTenant, Guid identityId)
    {
        ArgumentNullException.ThrowIfNull(personalTenant);
        if (personalTenant.Type != TenantType.Personal)
        {
            throw new InvalidOperationException("Personal tenant ownership requires a personal tenant.");
        }

        if (personalTenant.Id.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(personalTenant));
        }

        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        return new PersonalTenantOwnership
        {
            IdentityId = identityId,
            TenantId = personalTenant.Id
        };
    }
}
