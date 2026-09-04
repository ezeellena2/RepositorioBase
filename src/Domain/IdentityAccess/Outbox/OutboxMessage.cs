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

    public OutboxMessageStatus Status { get; private set; }

    /// <summary>Who holds the lease, and until when. A lapsed lease is claimable again without any cleanup pass.</summary>
    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>
    /// What a claim compares and swaps. Two workers reading the same due row both see this value; only the one
    /// whose conditional update still matches it wins, so a lease is never granted twice (IA-REQ-028).
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>Takes the lease for one dispatch attempt.</summary>
    public void Claim(string owner, DateTimeOffset now, TimeSpan leaseDuration) => throw new NotImplementedException();

    /// <summary>Gives the lease back without consuming an attempt, for a pass that ended before it delivered.</summary>
    public void ReleaseLease() => throw new NotImplementedException();

    /// <summary>Records a transient failure: one more attempt, a redacted code, and a later due time.</summary>
    public void Fail(string failureCode, DateTimeOffset now) => throw new NotImplementedException();

    public void MarkDelivered(DateTimeOffset now) => throw new NotImplementedException();

    /// <summary>Stops retrying. A message nothing will send again must be distinguishable from one still due.</summary>
    public void Abandon(string failureCode, DateTimeOffset now) => throw new NotImplementedException();

    public static OutboxMessage Create(string type, string payload, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Type = string.IsNullOrWhiteSpace(type) ? throw new ArgumentException("Message type cannot be empty.", nameof(type)) : type,
        Payload = payload ?? throw new ArgumentNullException(nameof(payload)),
        NextAttemptAt = now,
        CreatedAt = now
    };
}
