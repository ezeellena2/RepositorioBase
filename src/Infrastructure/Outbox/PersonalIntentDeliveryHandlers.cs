using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The two messages an anonymous Personal signup produces. As with the organization pair, the recipient is read from
/// the intent rather than from the payload, so the address never enters a stored envelope (IA-REQ-029), and the link
/// is the same `/confirm-email` screen every other confirmation uses (IA-REQ-005).
/// </summary>
public sealed class PersonalIntentConfirmationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterPersonalCommandHandler.IntentConfirmationMessageType;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var recipient = await PersonalIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/confirm-email");
        return await localizer.CreateAsync(
            recipient.Recipient,
            recipient.Language,
            deliveryLanguage,
            "PersonalRegistrationConfirmationSubject",
            "PersonalRegistrationConfirmationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }
}

public sealed class PersonalIntentSignInNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterPersonalCommandHandler.IntentSignInNoticeMessageType;

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var recipient = await PersonalIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/login");
        return await localizer.CreateAsync(
            recipient.Recipient,
            recipient.Language,
            deliveryLanguage,
            "PersonalRegistrationSignInSubject",
            "PersonalRegistrationSignInBody",
            cancellationToken,
            link);
    }
}

internal static class PersonalIntentRecipient
{
    internal static async Task<IntentRecipient?> ResolveAsync(ApplicationDbContext context, string payload, CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<RegisterPersonalCommandHandler.IntentEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IntentId);

        var recipient = await context.PendingPersonalIntents.AsNoTracking()
            .Where(intent => intent.Id == envelope.IntentId)
            .Select(intent => new IntentRecipient(intent.NormalizedEmail, intent.Language))
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(recipient?.Recipient) ? null : recipient;
    }
}
