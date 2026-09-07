namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public readonly record struct RoleId : IComparable<RoleId>
{
    private RoleId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static RoleId New() => new(Guid.NewGuid());

    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Role identifiers cannot be empty.", nameof(value))
        : new RoleId(value);

    /// <summary>
    /// Identifiers are ordered so that a keyset cursor can be written through the identifier itself, exactly as
    /// <c>MembershipId</c> already is. Reaching for the underlying value in a query instead — <c>role.Id.Value</c>
    /// — is a member access on a converted property, which EF cannot translate at all: the continuation page
    /// answered `500` until this existed. The comparison a page is actually decided by is PostgreSQL's, because
    /// both the ordering and the filter are translated; this implementation exists so the expression compiles and
    /// so anything comparing in memory agrees with itself.
    /// </summary>
    public int CompareTo(RoleId other) => Value.CompareTo(other.Value);

    public static bool operator <(RoleId left, RoleId right) => left.CompareTo(right) < 0;

    public static bool operator >(RoleId left, RoleId right) => left.CompareTo(right) > 0;

    public static bool operator <=(RoleId left, RoleId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(RoleId left, RoleId right) => left.CompareTo(right) >= 0;
}
