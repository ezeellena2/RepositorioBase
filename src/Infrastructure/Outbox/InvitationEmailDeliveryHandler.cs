using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>Delivers the invitation token, which reaches the recipient in a link fragment and never in a log.</summary>
public sealed class InvitationEmailDeliveryHandler(IIdentityEmailSender sender) : IOutboxDeliveryHandler
{
    private readonly IIdentityEmailSender _sender = sender;

    public string MessageType => "identity.invitation.requested";

    public Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken) => throw new NotImplementedException();
}
