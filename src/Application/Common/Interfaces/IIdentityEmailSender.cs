namespace CleanArchitecture.Application.Common.Interfaces;

/// <summary>
/// What the provider can tell us about an attempt, and all we are willing to keep: an opaque receipt and whether
/// the failure is worth retrying. No provider exception text is carried, because it ends up in a column
/// (IA-REQ-029).
/// </summary>
public sealed record EmailDeliveryReceipt(bool Delivered, string? ProviderReceipt, bool IsPermanentFailure);

/// <summary>
/// Sends one identity email. The idempotency key is the outbox message id, so a retry after an acknowledged send
/// can be reconciled with the provider instead of sending twice (IA-REQ-018).
/// </summary>
public interface IIdentityEmailSender
{
    Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken);
}
