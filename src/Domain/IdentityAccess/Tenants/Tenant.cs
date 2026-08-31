namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public sealed class Tenant : BaseEntity<TenantId>
{
    private Tenant()
    {
    }

    public TenantType Type { get; private set; }

    public TenantStatus Status { get; private set; }

    public TenantSlug Slug { get; private set; }

    public long AuthorizationVersion { get; private set; }

    public static Tenant CreateOrganization(TenantSlug slug) => Create(TenantType.Organization, slug);

    public static Tenant CreatePersonal(TenantSlug slug) => Create(TenantType.Personal, slug);

    public void Activate()
    {
        EnsureStatus(TenantStatus.PendingConfirmation, "Only pending tenants can be activated.");
        Status = TenantStatus.Active;
        IncrementAuthorizationVersion();
    }

    public void Suspend()
    {
        EnsureStatus(TenantStatus.Active, "Only active tenants can be suspended.");
        Status = TenantStatus.Suspended;
        IncrementAuthorizationVersion();
    }

    public void Close()
    {
        if (Status == TenantStatus.Closed)
        {
            throw new InvalidOperationException("Closed tenants cannot transition.");
        }

        Status = TenantStatus.Closed;
        IncrementAuthorizationVersion();
    }

    internal void IncrementAuthorizationVersion() => AuthorizationVersion++;

    private static Tenant Create(TenantType type, TenantSlug slug)
    {
        if (string.IsNullOrWhiteSpace(slug.Value))
        {
            throw new ArgumentException("Tenant slugs cannot be empty.", nameof(slug));
        }

        return new Tenant
        {
            Id = TenantId.New(),
            Type = type,
            Status = TenantStatus.PendingConfirmation,
            Slug = slug,
            AuthorizationVersion = 0
        };
    }

    private void EnsureStatus(TenantStatus expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException(message);
        }
    }
}
