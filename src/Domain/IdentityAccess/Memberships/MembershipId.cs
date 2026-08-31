namespace CleanArchitecture.Domain.IdentityAccess.Memberships;

public readonly record struct MembershipId
{
    private MembershipId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static MembershipId New() => new(Guid.NewGuid());

    public static MembershipId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Membership identifiers cannot be empty.", nameof(value))
        : new MembershipId(value);
}
