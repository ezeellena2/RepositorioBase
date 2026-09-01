using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.IdentityAccess.Outbox;

public sealed class OutboxSecret : BaseEntity<Guid>
{
    private OutboxSecret() { }

    public Guid OutboxMessageId { get; private set; }
    public string VersionedHash { get; private set; } = string.Empty;
    public string? Ciphertext { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public OutboxSecretStatus Status { get; private set; }
    public string? TerminalReason { get; private set; }
    public string? DeliveryReason { get; private set; }
    public string? ProviderReceipt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static OutboxSecret Create(Guid outboxMessageId, string versionedHash, string ciphertext, DateTimeOffset expiresAt)
    {
        if (outboxMessageId == Guid.Empty) throw new ArgumentException("Outbox message identifier cannot be empty.", nameof(outboxMessageId));
        if (string.IsNullOrWhiteSpace(versionedHash)) throw new ArgumentException("Versioned hash cannot be empty.", nameof(versionedHash));
        if (string.IsNullOrWhiteSpace(ciphertext)) throw new ArgumentException("Ciphertext cannot be empty.", nameof(ciphertext));
        return new OutboxSecret { Id = Guid.NewGuid(), OutboxMessageId = outboxMessageId, VersionedHash = versionedHash, Ciphertext = ciphertext, ExpiresAt = expiresAt, Status = OutboxSecretStatus.Pending };
    }

    public void MarkDelivered(string reason, DateTimeOffset deliveredAt, string providerReceipt)
    {
        if (Status != OutboxSecretStatus.Pending) throw new InvalidOperationException("Only pending secrets can be marked delivered.");
        DeliveryReason = RequireReason(reason, nameof(reason));
        ProviderReceipt = RequireReason(providerReceipt, nameof(providerReceipt));
        DeliveredAt = deliveredAt;
        Status = OutboxSecretStatus.Delivered;
        Ciphertext = null;
    }

    public void Consume(string reason, DateTimeOffset completedAt, string? receipt = null)
    {
        if (Status is not (OutboxSecretStatus.Pending or OutboxSecretStatus.Delivered)) throw new InvalidOperationException("Only pending or delivered secrets can be consumed.");
        if (Status == OutboxSecretStatus.Pending && !string.IsNullOrWhiteSpace(receipt)) throw new InvalidOperationException("Pending secrets cannot retain delivery evidence.");
        TerminalReason = RequireReason(reason, nameof(reason));
        ProviderReceipt ??= string.IsNullOrWhiteSpace(receipt) ? null : receipt;
        CompletedAt = completedAt;
        Status = OutboxSecretStatus.Consumed;
        Ciphertext = null;
    }

    public void Terminate(OutboxSecretStatus status, string reason, DateTimeOffset completedAt)
    {
        if (status is not (OutboxSecretStatus.Expired or OutboxSecretStatus.Failed)) throw new ArgumentException("Expired or failed status required.", nameof(status));
        if (Status is not (OutboxSecretStatus.Pending or OutboxSecretStatus.Delivered)) throw new InvalidOperationException("Only pending or delivered secrets can terminate.");
        Status = status;
        TerminalReason = RequireReason(reason, nameof(reason));
        CompletedAt = completedAt;
        Ciphertext = null;
    }

    private static string RequireReason(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Reason cannot be empty.", parameterName) : value;
}
