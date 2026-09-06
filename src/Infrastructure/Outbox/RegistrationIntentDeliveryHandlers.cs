using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
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
public sealed class RegistrationIntentConfirmationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterOrganizationCommandHandler.IntentConfirmationMessageType;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var recipient = await RegistrationIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/confirm-email");
        return new IdentityEmail(
            recipient,
            "Confirm your email",
            $"Open this link to finish registering your organization: {link}#token={Uri.EscapeDataString(token!)}");
    }
}

/// <summary>
/// What an address that already has an account receives instead. It carries no token, so holding it grants
/// nothing: whoever submitted the address is not proven to own it, and the owner is told only that they can sign
/// in and register from there.
/// </summary>
public sealed class RegistrationIntentSignInNoticeDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    public string MessageType => RegisterOrganizationCommandHandler.IntentSignInNoticeMessageType;

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var recipient = await RegistrationIntentRecipient.ResolveAsync(context, payload, cancellationToken);
        if (recipient is null) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/login");
        return new IdentityEmail(
            recipient,
            "Sign in to your account",
            $"Someone tried to register an organization with your email. Your account already exists; sign in at {link} and register from there.");
    }
}

internal static class RegistrationIntentRecipient
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<string?> ResolveAsync(ApplicationDbContext context, string payload, CancellationToken cancellationToken)
    {
        RegisterOrganizationCommandHandler.IntentEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RegisterOrganizationCommandHandler.IntentEnvelope>(payload, Options); }
        catch (JsonException) { return null; }
        if (envelope is null || envelope.IntentId == Guid.Empty) return null;

        var recipient = await context.PendingRegistrationIntents.AsNoTracking()
            .Where(intent => intent.Id == envelope.IntentId)
            .Select(intent => intent.NormalizedEmail)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(recipient) ? null : recipient;
    }

    /// <summary>The public origin comes from allowlisted configuration, never from a request header (SPEC section 8).</summary>
    internal static string Link(IdentityEmailOptions options, string path) =>
        new Uri(new Uri(options.PublicOrigin!, UriKind.Absolute), path).AbsoluteUri;
}
