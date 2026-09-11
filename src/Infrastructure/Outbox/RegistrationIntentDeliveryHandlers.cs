using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The two messages an anonymous registration produces. They are the only place the two branches differ, and the
/// difference is readable by exactly one person: the owner of the address that was submitted.
/// <para>
/// Both read the recipient from the intent rather than from the payload, so the address is never written into a
/// stored envelope that a Platform projection or an operator query could later surface (IA-REQ-029).
/// </para>
/// </summary>
public sealed class RegistrationIntentConfirmationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterOrganizationCommandHandler.IntentConfirmationMessageType;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var recipient = await RegistrationIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/confirm-email");
        return await localizer.CreateAsync(
            recipient.Recipient,
            recipient.Language,
            deliveryLanguage,
            "OrganizationRegistrationConfirmationSubject",
            "OrganizationRegistrationConfirmationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }
}

/// <summary>
/// What an address that already has an account receives instead. It carries no token, so holding it grants
/// nothing: whoever submitted the address is not proven to own it, and the owner is told only that they can sign
/// in and register from there.
/// </summary>
public sealed class RegistrationIntentSignInNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterOrganizationCommandHandler.IntentSignInNoticeMessageType;

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var recipient = await RegistrationIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/login");
        return await localizer.CreateAsync(
            recipient.Recipient,
            recipient.Language,
            deliveryLanguage,
            "OrganizationRegistrationSignInSubject",
            "OrganizationRegistrationSignInBody",
            cancellationToken,
            link);
    }
}

internal static class RegistrationIntentRecipient
{
    internal static async Task<IntentRecipient?> ResolveAsync(ApplicationDbContext context, string payload, CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<RegisterOrganizationCommandHandler.IntentEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IntentId);

        var recipient = await context.PendingRegistrationIntents.AsNoTracking()
            .Where(intent => intent.Id == envelope.IntentId)
            .Select(intent => new IntentRecipient(intent.NormalizedEmail, intent.Language))
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(recipient?.Recipient) ? null : recipient;
    }

    /// <summary>The public origin comes from allowlisted configuration, never from a request header (SPEC section 8).</summary>
    internal static string Link(IdentityEmailOptions options, string path) =>
        new Uri(new Uri(options.PublicOrigin!, UriKind.Absolute), path).AbsoluteUri;
}

internal sealed record IntentRecipient(string Recipient, string? Language);
