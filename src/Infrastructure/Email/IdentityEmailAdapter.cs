using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.Email;

/// <summary>
/// The configured provider. Production fails closed without email configuration rather than silently dropping
/// every confirmation and invitation on the floor.
/// </summary>
public sealed class IdentityEmailAdapter : IIdentityEmailSender
{
    public Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken) => throw new NotImplementedException();
}
