namespace CleanArchitecture.Domain.IdentityAccess.Sessions;

public readonly record struct UserSessionId
{
    private UserSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static UserSessionId New() => new(Guid.NewGuid());

    public static UserSessionId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Session identifiers cannot be empty.", nameof(value))
        : new UserSessionId(value);
}
