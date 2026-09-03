namespace CleanArchitecture.Domain.IdentityAccess.Invitations;

public readonly record struct InvitationId
{
    private InvitationId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static InvitationId New() => new(Guid.NewGuid());

    public static InvitationId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Invitation identifiers cannot be empty.", nameof(value))
        : new InvitationId(value);
}
