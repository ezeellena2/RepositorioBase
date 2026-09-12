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
    public DateTimeOffset? FirstAttemptAt { get; private set; }
    public string? RequestFingerprint { get; private set; }
    /// <summary>
    /// The supported language bound when delivery is first prepared. It is delivery metadata rather than payload
    /// content, and keeps retries stable when an account preference changes after the first attempt (IA-REQ-059).
    /// </summary>
    public string? DeliveryLanguage { get; private set; }
    public string? TraceId { get; private set; }

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

    /// <summary>
    /// How many times a message is worth retrying before it is nobody's business any more, and the schedule
    /// between attempts: 30 seconds doubling to a half-hour ceiling. The ceiling matters more than the curve —
    /// without it the eighth attempt would land an hour and a half out, long after anyone stopped caring.
    /// </summary>
    private const int MaxAttempts = 8;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A message is born due and pending: the business transaction that wrote it has already committed, so there
    /// is nothing left to wait for before the first attempt.
    /// </summary>
    public static OutboxMessage Create(string type, string payload, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Type = string.IsNullOrWhiteSpace(type) ? throw new ArgumentException("Message type cannot be empty.", nameof(type)) : type,
        Payload = payload ?? throw new ArgumentNullException(nameof(payload)),
        Status = OutboxMessageStatus.Pending,
        NextAttemptAt = now,
        CreatedAt = now
    };

    /// <summary>
    /// Takes the lease for one dispatch attempt. The generation moves with it, which is what a claim compares and
    /// swaps: two workers that read the same due row cannot both leave it holding their own lease.
    /// </summary>
    public void Claim(string owner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (Status != OutboxMessageStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending message can be claimed.");
        }

        LeaseOwner = owner;
        LeaseExpiresAt = now.Add(leaseDuration);
        Generation++;
    }

    /// <summary>Gives the lease back without spending an attempt, for a pass that ended before it delivered.</summary>
    public void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    public void BindDeliveryLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        EnsurePending();

        if (DeliveryLanguage is not null && !string.Equals(DeliveryLanguage, language, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A prepared delivery cannot change language.");
        }

        DeliveryLanguage = language;
    }

    /// <summary>
    /// Records a transient failure. The attempt is spent, the lease goes back so another worker may take the
    /// retry, and the next attempt is scheduled. Exhausting the budget is terminal: a message nothing will send
    /// again has to be distinguishable from one still waiting its turn.
    /// </summary>
    public void Fail(string failureCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsurePending();
        AttemptCount++;
        FailureCode = failureCode;
        ReleaseLease();

        if (AttemptCount >= MaxAttempts)
        {
            Status = OutboxMessageStatus.Abandoned;
            return;
        }

        NextAttemptAt = now.Add(BackoffFor(AttemptCount));
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        EnsurePending();
        Status = OutboxMessageStatus.Delivered;
        DeliveredAt = now;
        ReleaseLease();
    }

    /// <summary>
    /// Stops retrying now rather than after the whole budget: a provider that will never accept this message, or
    /// an envelope whose window has closed, is not worth seven more attempts.
    /// </summary>
    public void Abandon(string failureCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsurePending();
        AttemptCount++;
        FailureCode = failureCode;
        Status = OutboxMessageStatus.Abandoned;
        ReleaseLease();
    }

    /// <summary>Attempt n waits 30s * 2^(n-1), never longer than half an hour.</summary>
    private static TimeSpan BackoffFor(int attempt)
    {
        var scaled = FirstBackoff * Math.Pow(2, attempt - 1);
        return scaled > MaximumBackoff ? MaximumBackoff : scaled;
    }

    private void EnsurePending()
    {
        if (Status != OutboxMessageStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending message can change its dispatch state.");
        }
    }
}
