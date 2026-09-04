using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Invitations;

/// <summary>
/// The delivery half of an invitation, shared by every use case that establishes or withdraws an offer.
/// <para>
/// It exists so that minting a token, sealing it and invalidating whatever it replaces are one operation rather
/// than three things each handler has to remember. Forgetting the third is the dangerous one: a pending envelope
/// left behind is a withdrawn or rotated token that the dispatcher will still deliver (IA-REQ-015/018).
/// </para>
/// </summary>
internal static class InvitationDelivery
{
    internal const string MessageType = "identity.invitation.requested";

    /// <summary>Npgsql stores timestamps to microsecond precision; a finer reading would not survive the round trip.</summary>
    internal static DateTimeOffset ToStorablePrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    /// <summary>
    /// Mints a token and its hash together. They are returned as one value so a caller cannot hold a hash whose
    /// token it has lost, or a token it forgot to hash — the aggregate takes the hash, the envelope takes the
    /// token, and both come from here.
    /// </summary>
    internal static MintedToken Mint(ISecureTokenGenerator tokens, ITokenHasher tokenHasher)
    {
        var rawToken = tokens.Generate();
        return new MintedToken(rawToken, VersionedTokenHash.FromPersistedValue(tokenHasher.Hash(rawToken)));
    }

    /// <summary>
    /// Writes the message that will deliver an offer and the encrypted envelope holding its token. The payload
    /// names the invitation and nothing secret; the token exists only inside the envelope (IA-REQ-018/027).
    /// </summary>
    internal static void Deliver(
        IApplicationDbContext context,
        IOutboxSecretWriter secretWriter,
        MintedToken minted,
        InvitationId invitationId,
        TenantId tenantId,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var outbox = OutboxMessage.Create(MessageType, JsonSerializer.Serialize(new InvitationEnvelope(invitationId.Value, tenantId.Value)), now);
        context.OutboxMessages.Add(outbox);
        context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, minted.Hash.Value, secretWriter.Encrypt(minted.RawToken), expiresAt));
    }

    internal readonly record struct MintedToken(string RawToken, VersionedTokenHash Hash);

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

    /// <summary>Resolves an invitation from a submitted token without ever comparing raw values.</summary>
    internal static async Task<Invitation?> FindByTokenAsync(IApplicationDbContext context, ITokenHasher tokenHasher, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = tokenHasher.Hash(token);
        var invitation = await context.Invitations
            .Include(candidate => candidate.Roles)
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == VersionedTokenHash.FromPersistedValue(hash), cancellationToken);

        // The stored hash is compared again in constant time, so a lookup that matched only by index equality
        // cannot stand in for verifying the token itself.
        return invitation is not null && invitation.TokenHash.Matches(token) ? invitation : null;
    }

    internal sealed record InvitationEnvelope(Guid InvitationId, Guid TenantId);
}
