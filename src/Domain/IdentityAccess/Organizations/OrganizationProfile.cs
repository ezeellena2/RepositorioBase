using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

public sealed class OrganizationProfile
{
    private OrganizationProfile()
    {
    }

    public TenantId TenantId { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public NormalizedCuit Cuit { get; private set; }

    public static OrganizationProfile Create(Tenant tenant, string legalName, NormalizedCuit cuit)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Type != TenantType.Organization)
        {
            throw new InvalidOperationException("Organization profiles require an organization tenant.");
        }

        if (string.IsNullOrWhiteSpace(legalName))
        {
            throw new ArgumentException("Legal name cannot be empty.", nameof(legalName));
        }

        if (tenant.Id.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(tenant));
        }

        if (string.IsNullOrWhiteSpace(cuit.Value))
        {
            throw new ArgumentException("CUIT cannot be empty.", nameof(cuit));
        }

        return new OrganizationProfile
        {
            TenantId = tenant.Id,
            LegalName = legalName.Trim(),
            Cuit = cuit
        };
    }
}
