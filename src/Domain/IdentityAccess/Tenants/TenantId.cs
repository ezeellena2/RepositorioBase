namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public readonly record struct TenantId : IComparable<TenantId>
{
    private TenantId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static TenantId New() => new(Guid.NewGuid());

    public static TenantId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(value))
        : new TenantId(value);

    /// <summary>
    /// Identifiers are ordered so that a keyset cursor can be written through the identifier itself. Comparing
    /// the raw column instead makes EF push the cursor parameter back through the value converter, which
    /// cannot turn a plain Guid into one of these — ordering here is what keeps the query in the typed world.
    /// </summary>
    public int CompareTo(TenantId other) => Value.CompareTo(other.Value);

    public static bool operator <(TenantId left, TenantId right) => left.CompareTo(right) < 0;

    public static bool operator >(TenantId left, TenantId right) => left.CompareTo(right) > 0;

    public static bool operator <=(TenantId left, TenantId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TenantId left, TenantId right) => left.CompareTo(right) >= 0;
}
