using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
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
public sealed class PlatformInvitationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.requested";

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<InvitationEnvelope>(payload);
        var id = OutboxPayload.RequireId(envelope.InvitationId, PlatformAdminInvitationId.From);
        var invitation = await context.PlatformAdminInvitations.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.NormalizedEmail, item.IsOwner, item.Language })
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is null) return null;

        var link = PlatformDeliveryJson.Link(options.Value, "/platform/invitations/register");
        var prefix = invitation.IsOwner ? "PlatformOwnerInvitation" : "PlatformAdministratorInvitation";
        return await localizer.CreateAsync(
            invitation.NormalizedEmail,
            invitation.Language,
            deliveryLanguage,
            $"{prefix}Subject",
            $"{prefix}Body",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }

    private sealed record InvitationEnvelope(Guid InvitationId);
}

/// <summary>Carries the confirmation token to an identity created while answering a Platform invitation.</summary>
public sealed class PlatformConfirmationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.confirmation.requested";

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<ConfirmationEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IdentityId);

        var recipient = await context.Users.AsNoTracking()
            .Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        string? snapshotLanguage = null;
        if (envelope.InvitationId != Guid.Empty)
        {
            var invitationId = OutboxPayload.RequireId(envelope.InvitationId, PlatformAdminInvitationId.From);
            snapshotLanguage = await context.PlatformAdminInvitations.AsNoTracking()
                .Where(invitation => invitation.Id == invitationId)
                .Select(invitation => invitation.Language)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var link = PlatformDeliveryJson.Link(options.Value, "/platform/invitations/confirm");
        return await localizer.CreateAsync(
            recipient,
            snapshotLanguage,
            deliveryLanguage,
            "PlatformConfirmationSubject",
            "PlatformConfirmationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid InvitationId);
}

/// <summary>
/// The generic notice an address that already has an account receives. It carries no token or invitation details;
/// alongside the recipient identity identifier, its envelope retains only the InvitationId from the invitation to
/// resolve the immutable language snapshot. Whoever submitted that token is not proven to own the account, so the
/// owner is told only that they can sign in.
/// </summary>
public sealed class PlatformSignInNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    public string MessageType => "platform.invitation.signin.notice.requested";

    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<ConfirmationEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IdentityId);

        var recipient = await context.Users.AsNoTracking()
            .Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        string? snapshotLanguage = null;
        if (envelope.InvitationId != Guid.Empty)
        {
            var invitationId = OutboxPayload.RequireId(envelope.InvitationId, PlatformAdminInvitationId.From);
            snapshotLanguage = await context.PlatformAdminInvitations.AsNoTracking()
                .Where(invitation => invitation.Id == invitationId)
                .Select(invitation => invitation.Language)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var link = PlatformDeliveryJson.Link(options.Value, "/login");
        return await localizer.CreateAsync(
            recipient,
            snapshotLanguage,
            deliveryLanguage,
            "PlatformSignInSubject",
            "PlatformSignInBody",
            cancellationToken,
            link);
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid InvitationId);
}

internal static class PlatformDeliveryJson
{
    /// <summary>The public origin comes from allowlisted configuration, never from a request header (SPEC section 8).</summary>
    internal static string Link(IdentityEmailOptions options, string path) =>
        new Uri(new Uri(options.PublicOrigin!, UriKind.Absolute), path).AbsoluteUri;
}
