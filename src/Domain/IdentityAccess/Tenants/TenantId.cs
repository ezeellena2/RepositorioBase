namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public readonly record struct TenantId
{
    private TenantId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(value))
        : new TenantId(value);
}
