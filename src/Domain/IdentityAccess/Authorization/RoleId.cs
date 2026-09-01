namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public readonly record struct RoleId
{
    private RoleId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static RoleId New() => new(Guid.NewGuid());

    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Role identifiers cannot be empty.", nameof(value))
        : new RoleId(value);
}
