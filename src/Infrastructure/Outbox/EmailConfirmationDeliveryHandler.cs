using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// Delivers a confirmation token. It answers for both confirmation purposes — the organization registration's
/// and the invited registration's — because the message differs only in what the recipient is told next.
/// </summary>
public sealed class EmailConfirmationDeliveryHandler(IIdentityEmailSender sender) : IOutboxDeliveryHandler
{
    private readonly IIdentityEmailSender _sender = sender;

    public string MessageType => "identity.confirmation.requested";

    public Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken) => throw new NotImplementedException();
}
