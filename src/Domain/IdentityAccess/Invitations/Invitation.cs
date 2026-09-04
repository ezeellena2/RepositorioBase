using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Invitations;

/// <summary>
/// A temporary, single-use intent to add one identity to one organization (IA-REQ-014/015/017). The usable token
/// never enters the aggregate: only its versioned hash is modelled, so nothing here can carry a secret into
/// persistence, audit or a log.
/// </summary>
public sealed class Invitation : BaseEntity<InvitationId>
{
    private readonly List<InvitationRole> _roles = new();

    private Invitation() { }

    public TenantId TenantId { get; private set; }

    public string NormalizedEmail { get; private set; } = string.Empty;

    public VersionedTokenHash TokenHash { get; private set; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByIdentityId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<InvitationRole> Roles => _roles.AsReadOnly();

    public static Invitation Issue(Tenant tenant, string email, IEnumerable<Role> roles, VersionedTokenHash tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(roles);
        if (tenant.Type != TenantType.Organization)
        {
            throw new InvalidOperationException("Only organization tenants can invite members.");
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        var invitation = new Invitation
        {
            Id = InvitationId.New(),
            TenantId = tenant.Id,
            NormalizedEmail = Normalize(email),
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        invitation.Offer(tenant, roles);
        return invitation;
    }

    /// <summary>Expiry is derived, never stored, so a lapsed invitation stays reissuable in place.</summary>
    public bool IsPendingAt(DateTimeOffset now) => Status == InvitationStatus.Pending && now < ExpiresAt;

    /// <summary>
    /// Whether <paramref name="email"/> is this invitation's recipient, compared in the one canonical form
    /// <see cref="Normalize"/> defines. Acceptance requires an identity whose confirmed email matches the
    /// recipient (IA-REQ-016), and that comparison is the same canonicalization question the aggregate already
    /// answers when the invitation is issued — so it is answered here rather than restated by every caller.
    /// </summary>
    public bool IsAddressedTo(string? email)
    {
        if (email is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Normalize(email), NormalizedEmail, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // An address the aggregate would refuse to store is an address it can never have been issued to.
            return false;
        }
    }

    public void Accept(Tenant tenant, Guid identityId, DateTimeOffset now)
    {
        EnsureTenant(tenant);
        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        // The terminal short-circuit comes before every state guard: replaying a completed acceptance is the
        // caller retrying, and it must answer idempotently rather than claim the invitation expired meanwhile
        // (IA-REQ-016). Only a different identity turns the replay into a failure.
        if (Status == InvitationStatus.Accepted)
        {
            if (AcceptedByIdentityId != identityId)
            {
                throw new InvalidOperationException("An invitation can only be accepted by the identity that accepted it.");
            }

            return;
        }

        if (Status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending invitations can be accepted.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (!IsPendingAt(now))
        {
            throw new InvalidOperationException("Expired invitations cannot be accepted.");
        }

        Status = InvitationStatus.Accepted;
        AcceptedByIdentityId = identityId;
        AcceptedAt = now;
    }

    public void Cancel(Tenant tenant, DateTimeOffset now)
    {
        EnsureTenant(tenant);
        if (Status == InvitationStatus.Cancelled)
        {
            return;
        }

        if (Status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending invitations can be cancelled.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        Status = InvitationStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>
    /// Rotates the token and its window together. Both halves move in one transition, so there is never an
    /// instant with two usable tokens for one invitation, and a lapsed invitation is revived in place instead of
    /// leaving a second pending row for the same recipient (IA-REQ-017).
    /// </summary>
    public void Reissue(Tenant tenant, VersionedTokenHash tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        EnsureTenant(tenant);
        if (Status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending invitations can be reissued.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        if (tokenHash == TokenHash)
        {
            // Reissuing to the same hash would extend the window while leaving the previous token valid, which
            // is exactly what a reissue exists to prevent.
            throw new ArgumentException("Reissuing an invitation must rotate its token.", nameof(tokenHash));
        }

        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    private void Offer(Tenant tenant, IEnumerable<Role> roles)
    {
        foreach (var role in roles)
        {
            ArgumentNullException.ThrowIfNull(role);
            if (_roles.Exists(offered => offered.RoleId == role.Id))
            {
                continue;
            }

            _roles.Add(InvitationRole.Create(tenant, Id, role));
        }

        if (_roles.Count == 0)
        {
            throw new ArgumentException("Invitations must offer at least one role.", nameof(roles));
        }
    }

    private void EnsureTenant(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id != TenantId)
        {
            throw new InvalidOperationException("Invitations can only be changed within their tenant.");
        }
    }

    /// <summary>
    /// Produces the single canonical form of a recipient, and refuses any address whose canonical form the
    /// database could not verify for itself. The policy is Unicode with NFC, and this method is its authority.
    /// <para>
    /// Composition comes first: <c>josé</c> and <c>josé</c> are the same address written two ways, and
    /// without normalizing they are different bytes and would each take a pending slot. NFC is chosen because
    /// PostgreSQL can verify it exactly, with <c>normalize(x, NFC)</c>, so the database holds the same rule
    /// rather than trusting this one.
    /// </para>
    /// <para>
    /// Case comes second, and is a refusal rather than a mapping. PostgreSQL's <c>lower()</c> is locale-aware and
    /// .NET's invariant mapping is not, so the two disagree on real characters — U+0130 survives
    /// <c>ToLowerInvariant</c> unchanged but is folded by <c>lower()</c>. Rather than ask the database to imitate
    /// .NET, which it cannot, the aggregate emits nothing <c>lower()</c> would change: no uppercase or titlecase
    /// category, and no whitespace of any kind.
    /// </para>
    /// </summary>
    /// <summary>
    /// The canonical form of a recipient, for a caller that has to compare or look one up before an invitation
    /// exists to ask. The rule stays here, in the type that owns it, so a caller cannot hold a second copy of it
    /// that drifts from the one persistence enforces.
    /// </summary>
    public static string Canonicalize(string email) => Normalize(email);

    private static string Normalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Invitation recipients cannot be empty.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC);
        if (normalized.Length > 256 || !normalized.Contains('@', StringComparison.Ordinal) || !IsCanonical(normalized))
        {
            throw new ArgumentException("Invitation recipients must be a normalizable email address.", nameof(email));
        }

        return normalized;
    }

    private static bool IsCanonical(string normalized)
    {
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsUpper(character) ||
                char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.TitlecaseLetter)
            {
                return false;
            }
        }

        return true;
    }
}
