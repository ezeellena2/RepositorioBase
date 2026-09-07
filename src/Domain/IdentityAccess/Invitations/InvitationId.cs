namespace CleanArchitecture.Domain.IdentityAccess.Invitations;

public readonly record struct InvitationId : IComparable<InvitationId>
{
    private InvitationId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static InvitationId New() => new(Guid.NewGuid());

    public static InvitationId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Invitation identifiers cannot be empty.", nameof(value))
        : new InvitationId(value);

    /// <summary>
    /// Ordered for the same reason <c>MembershipId</c> is: a keyset cursor is written through the identifier
    /// itself, because reaching for the underlying value in a query is a member access on a converted property
    /// and EF cannot translate it. The page boundary is decided by PostgreSQL, which compares both the ordering
    /// and the filter; this implementation is what lets the expression exist.
    /// </summary>
    public int CompareTo(InvitationId other) => Value.CompareTo(other.Value);

    public static bool operator <(InvitationId left, InvitationId right) => left.CompareTo(right) < 0;

    public static bool operator >(InvitationId left, InvitationId right) => left.CompareTo(right) > 0;

    public static bool operator <=(InvitationId left, InvitationId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(InvitationId left, InvitationId right) => left.CompareTo(right) >= 0;
}
