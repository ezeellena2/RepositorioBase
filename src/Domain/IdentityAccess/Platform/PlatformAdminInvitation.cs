using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// A single-use intent to make one identity a Platform administrator (IA-REQ-040/041/042).
/// <para>
/// It is deliberately not an organization <see cref="Invitation"/>. Sharing that type would put Platform
/// authority behind the `members.*` permissions and behind every path that issues, resends or withdraws a member
/// offer, and it would give an organization invitation the delivery state and bound user only this one needs.
/// What it does share is the recipient canonicalization: one rule, in the type that owns it, so the two cannot
/// disagree about which addresses are the same address.
/// </para>
/// <para>
/// As with an invitation, the usable token never enters the aggregate — only its versioned hash — so nothing here
/// can carry a secret into persistence, audit or a log.
/// </para>
/// </summary>
public sealed class PlatformAdminInvitation : BaseEntity<PlatformAdminInvitationId>
{
    private PlatformAdminInvitation() { }

    public TenantId TenantId { get; private set; }

    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>The issuing request's supported language, captured once and preserved across reissue.</summary>
    public string? Language { get; private set; }

    public VersionedTokenHash TokenHash { get; private set; }

    public PlatformAdminInvitationStatus Status { get; private set; }

    public PlatformAdminInvitationDelivery Delivery { get; private set; }

    /// <summary>Whether this is the bootstrap owner's invitation, which is the one recovery may rotate.</summary>
    public bool IsOwner { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// The identity this invitation now belongs to, set when the recipient registers or is matched. It exists so
    /// the MFA gates can require that the caller is the invitee rather than merely someone holding the token
    /// (IA-REQ-041), and it stays null for an invitation whose recipient has no account yet.
    /// </summary>
    public Guid? BoundIdentityId { get; private set; }

    public DateTimeOffset? BoundAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>The outbox message carrying this invitation's token, so its delivery outcome can be read back.</summary>
    public Guid? DeliveryMessageId { get; private set; }

    public DateTimeOffset? DeliverySettledAt { get; private set; }

    public static PlatformAdminInvitation Issue(
        Tenant tenant,
        string email,
        VersionedTokenHash tokenHash,
        bool isOwner,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        Issue(tenant, email, tokenHash, isOwner, "en", now, expiresAt);

    public static PlatformAdminInvitation Issue(
        Tenant tenant,
        string email,
        VersionedTokenHash tokenHash,
        bool isOwner,
        string language,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (tenant.Type != TenantType.Platform)
        {
            throw new InvalidOperationException("Only the Platform tenant can invite administrators.");
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        if (tokenHash.IsEmpty)
        {
            throw new ArgumentException("A Platform invitation must carry a token hash.", nameof(tokenHash));
        }

        return new PlatformAdminInvitation
        {
            Id = PlatformAdminInvitationId.New(),
            TenantId = tenant.Id,
            NormalizedEmail = Canonicalize(email),
            Language = language,
            TokenHash = tokenHash,
            Status = PlatformAdminInvitationStatus.Pending,
            Delivery = PlatformAdminInvitationDelivery.Pending,
            IsOwner = isOwner,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>The one canonical form of a recipient, which is the same rule an organization invitation uses.</summary>
    public static string Canonicalize(string email) => Invitation.Canonicalize(email);

    public bool IsPendingAt(DateTimeOffset now) => Status == PlatformAdminInvitationStatus.Pending && now < ExpiresAt;

    public bool IsAddressedTo(string? email)
    {
        if (email is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Canonicalize(email), NormalizedEmail, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether bootstrap recovery may rotate this invitation. Only a still-pending offer that has run out of time
    /// or whose delivery permanently failed qualifies: rotating one that is merely in flight would invalidate a
    /// token the recipient may be about to use, which is the opposite of recovering it (IA-REQ-040).
    /// </summary>
    public bool IsRecoverableAt(DateTimeOffset now) =>
        Status == PlatformAdminInvitationStatus.Pending &&
        (now >= ExpiresAt || Delivery == PlatformAdminInvitationDelivery.PermanentlyFailed);

    /// <summary>Records which message carries the current token, and what became of it.</summary>
    public void RecordDelivery(PlatformAdminInvitationDelivery delivery, Guid? messageId, DateTimeOffset now)
    {
        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        Delivery = delivery;
        if (messageId is { } id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Delivery message identifiers cannot be empty.", nameof(messageId));
            }

            DeliveryMessageId = id;
        }

        DeliverySettledAt = delivery == PlatformAdminInvitationDelivery.Pending ? null : now;
    }

    /// <summary>
    /// Binds the invitation to the identity that answered it. Rebinding to the same identity is the caller
    /// retrying and answers idempotently; rebinding to a different one is refused, because a token holder must
    /// not be able to move a standing Platform offer onto another account.
    /// </summary>
    public void Bind(Guid identityId, DateTimeOffset now)
    {
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        if (Status != PlatformAdminInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending Platform invitations can be bound.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (BoundIdentityId is { } bound)
        {
            if (bound != identityId)
            {
                throw new InvalidOperationException("A Platform invitation can only be bound to one identity.");
            }

            return;
        }

        BoundIdentityId = identityId;
        BoundAt = now;
    }

    /// <summary>
    /// Consumes the invitation as the membership becomes active. Replaying it for the same identity is the caller
    /// retrying and answers idempotently, exactly as accepting an organization invitation does.
    /// </summary>
    public void Accept(Guid identityId, DateTimeOffset now)
    {
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        if (Status == PlatformAdminInvitationStatus.Accepted)
        {
            if (BoundIdentityId != identityId)
            {
                throw new InvalidOperationException("A Platform invitation can only be accepted by the identity it was bound to.");
            }

            return;
        }

        if (Status != PlatformAdminInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending Platform invitations can be accepted.");
        }

        if (!IsPendingAt(now))
        {
            throw new InvalidOperationException("Expired Platform invitations cannot be accepted.");
        }

        if (BoundIdentityId is { } bound && bound != identityId)
        {
            throw new InvalidOperationException("A Platform invitation can only be accepted by the identity it was bound to.");
        }

        Bind(identityId, now);
        Status = PlatformAdminInvitationStatus.Accepted;
        AcceptedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == PlatformAdminInvitationStatus.Cancelled)
        {
            return;
        }

        if (Status != PlatformAdminInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending Platform invitations can be cancelled.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        Status = PlatformAdminInvitationStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>
    /// Rotates the token and its window together, so there is never an instant with two usable tokens for one
    /// invitation. Delivery returns to pending because the replacement has not been sent yet — leaving it on a
    /// permanent failure would make the fresh invitation instantly recoverable again.
    /// </summary>
    public void Reissue(VersionedTokenHash tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (Status != PlatformAdminInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending Platform invitations can be reissued.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        if (tokenHash.IsEmpty)
        {
            throw new ArgumentException("A Platform invitation must carry a token hash.", nameof(tokenHash));
        }

        if (tokenHash == TokenHash)
        {
            throw new ArgumentException("Reissuing a Platform invitation must rotate its token.", nameof(tokenHash));
        }

        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        Delivery = PlatformAdminInvitationDelivery.Pending;
        DeliveryMessageId = null;
        DeliverySettledAt = null;
    }
}
