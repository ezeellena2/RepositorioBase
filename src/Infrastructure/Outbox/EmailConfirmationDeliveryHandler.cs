using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// Delivers a confirmation token. It answers for the organization registration's purpose; the invited
/// registration writes its own type and is handled beside it, because the two differ only in what the recipient
/// is told to do next.
/// </summary>
public sealed class EmailConfirmationDeliveryHandler(ApplicationDbContext context, IIdentityEmailSender sender) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.confirmation.requested";

    public async Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload, PayloadFormat)
            ?? throw new InvalidOperationException("A confirmation payload must name its identity.");
        var recipient = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return new EmailDeliveryReceipt(false, null, IsPermanentFailure: true);
        }

        return await sender.SendAsync(
            recipient,
            "Confirm your email",
            $"Open this link to confirm: /confirm-email#token={token}",
            outboxMessageId.ToString(),
            cancellationToken);
    }

    /// <summary>
    /// Payloads are read case-insensitively. The writer and the reader are separate types on separate
    /// sides of a queue, and a casing mismatch between them would present as an endless retry rather
    /// than as the contract error it is.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadFormat = new() { PropertyNameCaseInsensitive = true };

    private sealed record ConfirmationEnvelope(Guid IdentityId);
}
