using CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The reset link. The recipient is read from the identity the request belongs to rather than from the payload, so
/// the address never enters a stored envelope, and the token arrives in the fragment where no server logs it.
/// </summary>
public sealed class PasswordRecoveryDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RequestPasswordRecoveryCommandHandler.MessageType;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<RequestPasswordRecoveryCommandHandler.Envelope>(payload);
        OutboxPayload.RequireId(envelope.RequestId);

        var recipient = await (from request in context.PasswordResetRequests.AsNoTracking()
                               join user in context.Users.AsNoTracking() on request.IdentityId equals user.Id
                               where request.Id == envelope.RequestId
                               select user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/credentials/reset");
        return await localizer.CreateAsync(
            recipient,
            null,
            deliveryLanguage,
            "PasswordRecoverySubject",
            "PasswordRecoveryBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }
}
