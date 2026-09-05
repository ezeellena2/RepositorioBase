using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// The delivery half of a Platform invitation, shared by bootstrap, recovery and administrator invitation.
/// <para>
/// It mirrors the organization invitation's delivery deliberately — minting, sealing and invalidating are one
/// operation rather than three each caller has to remember — but keeps its own message types. Sharing them would
/// make the dispatcher and the confirmation handler guess which kind of invitation an envelope belongs to, and a
/// guess that lands on the wrong one would confirm a Platform invitee through an organization's path.
/// </para>
/// </summary>
internal static class PlatformInvitationDelivery
{
    /// <summary>Carries the invitation token to its recipient.</summary>
    internal const string MessageType = "platform.invitation.requested";

    /// <summary>Carries the email-confirmation token to an identity created while answering a Platform invitation.</summary>
    internal const string ConfirmationMessageType = "platform.invitation.confirmation.requested";

    /// <summary>
    /// The generic notice an address that already has an account receives. Holding the invitation token is not
    /// proof of owning that account, so the notice carries nothing and tells the real owner only that they can
    /// sign in. Writing it is also what makes the existing branch cost the same work as the missing one.
    /// </summary>
    internal const string ExistingIdentityNoticeMessageType = "platform.invitation.signin.notice.requested";

    /// <summary>Npgsql stores timestamps to microsecond precision; a finer reading would not survive the round trip.</summary>
    internal static DateTimeOffset ToStorablePrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    internal static MintedToken Mint(ISecureTokenGenerator tokens, ITokenHasher tokenHasher)
    {
        var rawToken = tokens.Generate();
        return new MintedToken(rawToken, tokenHasher.Of(rawToken));
    }

    /// <summary>
    /// Writes the message that will carry an offer and the encrypted envelope holding its token, and records on
    /// the invitation which message that is — recovery reads that back to learn whether delivery failed for good.
    /// </summary>
    internal static void Deliver(
        IApplicationDbContext context,
        IOutboxSecretWriter secretWriter,
        PlatformAdminInvitation invitation,
        MintedToken minted,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var outbox = OutboxMessage.Create(
            MessageType,
            JsonSerializer.Serialize(new PlatformInvitationEnvelope(invitation.Id.Value)),
            now);
        context.OutboxMessages.Add(outbox);
        context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, minted.Hash.Value, secretWriter.Encrypt(minted.RawToken), expiresAt));
        invitation.RecordDelivery(PlatformAdminInvitationDelivery.Pending, outbox.Id, now);
    }

    /// <summary>
    /// Retires the envelope holding a token that is about to stop resolving. The message stays as history; the
    /// ciphertext does not, because a terminalized envelope is what makes the superseded token undeliverable.
    /// </summary>
    internal static async Task RetireAsync(IApplicationDbContext context, VersionedTokenHash supersededHash, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var value = supersededHash.Value;
        var envelope = await context.OutboxSecrets
            .FirstOrDefaultAsync(secret => secret.VersionedHash == value && secret.Status == OutboxSecretStatus.Pending, cancellationToken);
        envelope?.Terminate(OutboxSecretStatus.Expired, reason, now);
    }

    /// <summary>Resolves a Platform invitation from a submitted token without ever comparing raw values.</summary>
    internal static async Task<PlatformAdminInvitation?> FindByTokenAsync(
        IApplicationDbContext context,
        ITokenHasher tokenHasher,
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = tokenHasher.Of(token);
        var invitation = await context.PlatformAdminInvitations
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        // The stored hash is compared again in constant time, so a lookup that matched only by index equality
        // cannot stand in for verifying the token itself.
        return invitation is not null && invitation.TokenHash.Matches(token) ? invitation : null;
    }

    /// <summary>
    /// Brings the invitation's delivery state up to date with what became of the message carrying its token.
    /// <para>
    /// The outbox is the one place that knows; reading it here rather than having the dispatcher write back keeps
    /// a single writer for a message's fate, and it means recovery decides against the state that actually holds
    /// at the moment it decides — not whatever a background job last managed to record.
    /// </para>
    /// </summary>
    internal static async Task SynchronizeDeliveryAsync(
        IApplicationDbContext context,
        PlatformAdminInvitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (invitation.DeliveryMessageId is not { } messageId)
        {
            return;
        }

        var message = await context.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == messageId, cancellationToken);
        if (message is null)
        {
            return;
        }

        var delivery = message.Status switch
        {
            OutboxMessageStatus.Delivered => PlatformAdminInvitationDelivery.Delivered,
            OutboxMessageStatus.Abandoned => PlatformAdminInvitationDelivery.PermanentlyFailed,
            _ => PlatformAdminInvitationDelivery.Pending
        };
        if (delivery != invitation.Delivery)
        {
            invitation.RecordDelivery(delivery, messageId, now);
        }
    }

    internal readonly record struct MintedToken(string RawToken, VersionedTokenHash Hash);

    internal sealed record PlatformInvitationEnvelope(Guid InvitationId);

    /// <summary>
    /// The confirmation purpose for an identity created while answering a Platform invitation. It names the
    /// invitation as well as the identity, so confirmation can require that the two still belong together and
    /// cannot be completed against a different offer.
    /// </summary>
    internal sealed record PlatformConfirmationEnvelope(Guid IdentityId, Guid InvitationId);
}
