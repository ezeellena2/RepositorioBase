using System.Text.Json;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The three Platform onboarding messages. Without these the dispatcher would find no handler for them and fail
/// each one closed — an invitation nobody could ever receive.
/// <para>
/// Each link points at the page that answers it, with the token in the fragment. A fragment is never sent to a
/// server and never appears in a proxy log, and the page erases it on arrival (IA-REQ-025/029).
/// </para>
/// </summary>
public sealed class PlatformInvitationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.requested";

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<InvitationEnvelope>(payload, PlatformDeliveryJson.Options);
        if (envelope is null || envelope.InvitationId == Guid.Empty) return null;

        var id = PlatformAdminInvitationId.From(envelope.InvitationId);
        var invitation = await context.PlatformAdminInvitations.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.NormalizedEmail, item.IsOwner })
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is null) return null;

        var link = PlatformDeliveryJson.Link(options.Value, "/platform/invitations/register");
        var role = invitation.IsOwner ? "the Platform owner" : "a Platform administrator";
        return new IdentityEmail(
            invitation.NormalizedEmail,
            "You have been invited to Platform",
            $"You have been invited to become {role}. Open this link to set up your account: {link}#token={Uri.EscapeDataString(token!)}");
    }

    private sealed record InvitationEnvelope(Guid InvitationId);
}

/// <summary>Carries the confirmation token to an identity created while answering a Platform invitation.</summary>
public sealed class PlatformConfirmationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.confirmation.requested";

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload, PlatformDeliveryJson.Options);
        if (envelope is null || envelope.IdentityId == Guid.Empty) return null;

        var recipient = await context.Users.AsNoTracking()
            .Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = PlatformDeliveryJson.Link(options.Value, "/platform/invitations/confirm");
        return new IdentityEmail(
            recipient,
            "Confirm your Platform address",
            $"Open this link to confirm your address: {link}#token={Uri.EscapeDataString(token!)}");
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid InvitationId);
}

/// <summary>
/// The generic notice an address that already has an account receives. It carries no token and names no
/// invitation: whoever submitted that token is not proven to own the account, so the owner is told only that they
/// can sign in.
/// </summary>
public sealed class PlatformSignInNoticeDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.signin.notice.requested";

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload, PlatformDeliveryJson.Options);
        if (envelope is null || envelope.IdentityId == Guid.Empty) return null;

        var recipient = await context.Users.AsNoTracking()
            .Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = PlatformDeliveryJson.Link(options.Value, "/login");
        return new IdentityEmail(
            recipient,
            "Sign in to your account",
            $"Someone tried to register with your email. Your account already exists; you can sign in at {link}.");
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid InvitationId);
}

internal static class PlatformDeliveryJson
{
    /// <summary>Production writes PascalCase and these envelopes are read back here; matching case-insensitively is what keeps a rename from silently retrying forever.</summary>
    internal static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The public origin comes from allowlisted configuration, never from a request header (SPEC section 8).</summary>
    internal static string Link(IdentityEmailOptions options, string path) =>
        new Uri(new Uri(options.PublicOrigin!, UriKind.Absolute), path).AbsoluteUri;
}
