using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.IdentityAccess.Outbox;

public sealed class OutboxMessage : BaseEntity<Guid>
{
    private OutboxMessage() { }

    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Type = string.IsNullOrWhiteSpace(type) ? throw new ArgumentException("Message type cannot be empty.", nameof(type)) : type,
        Payload = payload ?? throw new ArgumentNullException(nameof(payload)),
        NextAttemptAt = now,
        CreatedAt = now
    };
}
