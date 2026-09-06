using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The two messages an anonymous Personal signup produces. As with the organization pair, the recipient is read from
/// the intent rather than from the payload, so the address never enters a stored envelope (IA-REQ-029), and the link
/// is the same `/confirm-email` screen every other confirmation uses (IA-REQ-005).
/// </summary>
public sealed class PersonalIntentConfirmationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterPersonalCommandHandler.IntentConfirmationMessageType;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var recipient = await PersonalIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/confirm-email");
        return new IdentityEmail(
            recipient,
            "Confirm your email",
            $"Open this link to finish setting up your personal account: {link}#token={Uri.EscapeDataString(token!)}");
    }
}

public sealed class PersonalIntentSignInNoticeDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterPersonalCommandHandler.IntentSignInNoticeMessageType;

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var recipient = await PersonalIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/login");
        return new IdentityEmail(
            recipient,
            "Sign in to your account",
            $"Someone tried to set up a personal account with your email. Your account already exists; sign in at {link} and add it from there.");
    }
}

internal static class PersonalIntentRecipient
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<string?> ResolveAsync(ApplicationDbContext context, string payload, CancellationToken cancellationToken)
    {
        RegisterPersonalCommandHandler.IntentEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RegisterPersonalCommandHandler.IntentEnvelope>(payload, Options); }
        catch (JsonException) { return null; }
        if (envelope is null || envelope.IntentId == Guid.Empty) return null;

        var recipient = await context.PendingPersonalIntents.AsNoTracking()
            .Where(intent => intent.Id == envelope.IntentId)
            .Select(intent => intent.NormalizedEmail)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(recipient) ? null : recipient;
    }
}
