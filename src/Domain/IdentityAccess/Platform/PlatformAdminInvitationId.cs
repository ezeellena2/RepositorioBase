namespace CleanArchitecture.Domain.IdentityAccess.Platform;

public readonly record struct PlatformAdminInvitationId
{
    private PlatformAdminInvitationId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static PlatformAdminInvitationId New() => new(Guid.NewGuid());

    public static PlatformAdminInvitationId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Platform invitation identifiers cannot be empty.", nameof(value))
        : new PlatformAdminInvitationId(value);
}
