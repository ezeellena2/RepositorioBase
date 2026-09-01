namespace CleanArchitecture.Domain.IdentityAccess.Outbox;

public enum OutboxSecretStatus
{
    Pending = 0,
    Delivered = 1,
    Expired = 2,
    Failed = 3,
    Consumed = 4
}
