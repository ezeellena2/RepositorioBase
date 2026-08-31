namespace CleanArchitecture.Domain.IdentityAccess.Memberships;

public readonly record struct MembershipId(Guid Value)
{
    public static MembershipId New() => new(Guid.NewGuid());

    public static MembershipId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Membership identifiers cannot be empty.", nameof(value))
        : new MembershipId(value);
}
