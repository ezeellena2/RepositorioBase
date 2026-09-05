namespace CleanArchitecture.Domain.IdentityAccess.Memberships;

public readonly record struct MembershipId : IComparable<MembershipId>
{
    private MembershipId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static MembershipId New() => new(Guid.NewGuid());

    public static MembershipId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Membership identifiers cannot be empty.", nameof(value))
        : new MembershipId(value);

    /// <summary>
    /// Identifiers are ordered so that a keyset cursor can be written through the identifier itself. Comparing
    /// the raw column instead makes EF push the cursor parameter back through the value converter, which
    /// cannot turn a plain Guid into one of these — ordering here is what keeps the query in the typed world.
    /// </summary>
    public int CompareTo(MembershipId other) => Value.CompareTo(other.Value);

    public static bool operator <(MembershipId left, MembershipId right) => left.CompareTo(right) < 0;

    public static bool operator >(MembershipId left, MembershipId right) => left.CompareTo(right) > 0;

    public static bool operator <=(MembershipId left, MembershipId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(MembershipId left, MembershipId right) => left.CompareTo(right) >= 0;
}
