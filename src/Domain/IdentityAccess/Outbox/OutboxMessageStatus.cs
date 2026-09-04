namespace CleanArchitecture.Domain.IdentityAccess.Outbox;

/// <summary>
/// Where a message stands with the dispatcher. There is no "Failed" here on purpose: a transient failure leaves
/// the message pending with a later attempt time, and only exhausting its attempts makes it terminal — a state
/// nothing transitions into would rot exactly as an unreachable invitation state would.
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 0,
    Delivered = 1,
    Abandoned = 2
}
