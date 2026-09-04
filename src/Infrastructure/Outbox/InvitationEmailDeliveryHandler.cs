using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// Delivers an invitation token. The recipient is read from the invitation rather than carried in the payload:
/// an address is PII, and IA-REQ-029 keeps PII out of outbox payloads exactly as it keeps tokens out.
/// <para>
/// The token reaches the recipient in the link's fragment, which browsers do not send to the server and proxies
/// do not log, so following the link cannot spill it into someone else's access log.
/// </para>
/// </summary>
public sealed class InvitationEmailDeliveryHandler(ApplicationDbContext context, IIdentityEmailSender sender) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.invitation.requested";

    public async Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<InvitationEnvelope>(payload, PayloadFormat)
            ?? throw new InvalidOperationException("An invitation delivery payload must name its invitation.");
        var invitationId = InvitationId.From(envelope.InvitationId);
        var recipient = await context.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.Id == invitationId)
            .Select(invitation => invitation.NormalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient is null)
        {
            return new EmailDeliveryReceipt(false, null, IsPermanentFailure: true);
        }

        return await sender.SendAsync(
            recipient,
            "You have been invited",
            $"Open this link to accept: /invitations/accept#token={token}",
            outboxMessageId.ToString(),
            cancellationToken);
    }

    /// <summary>
    /// Payloads are read case-insensitively. The writer and the reader are separate types on separate
    /// sides of a queue, and a casing mismatch between them would present as an endless retry rather
    /// than as the contract error it is.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadFormat = new() { PropertyNameCaseInsensitive = true };

    private sealed record InvitationEnvelope(Guid InvitationId, Guid TenantId);
}
