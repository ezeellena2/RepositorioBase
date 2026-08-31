namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(value))
        : new TenantId(value);
}
