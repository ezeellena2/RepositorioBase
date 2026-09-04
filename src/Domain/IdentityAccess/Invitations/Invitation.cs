using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
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

    public string TokenHash { get; private set; } = string.Empty;

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByIdentityId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<InvitationRole> Roles => _roles.AsReadOnly();

    public static Invitation Issue(Tenant tenant, string email, IEnumerable<Role> roles, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
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
            TokenHash = RequireVersionedHash(tokenHash),
            Status = InvitationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        invitation.Offer(tenant, roles);
        return invitation;
    }

    /// <summary>Expiry is derived, never stored, so a lapsed invitation stays reissuable in place.</summary>
    public bool IsPendingAt(DateTimeOffset now) => Status == InvitationStatus.Pending && now < ExpiresAt;

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
    public void Reissue(Tenant tenant, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
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

        var rotated = RequireVersionedHash(tokenHash);
        if (string.Equals(rotated, TokenHash, StringComparison.Ordinal))
        {
            // Reissuing to the same hash would extend the window while leaving the previous token valid, which
            // is exactly what a reissue exists to prevent.
            throw new ArgumentException("Reissuing an invitation must rotate its token.", nameof(tokenHash));
        }

        TokenHash = rotated;
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
    /// database could not verify for itself.
    /// <para>
    /// PostgreSQL's <c>lower()</c> is locale-aware and .NET's invariant mapping is not, so the two disagree on a
    /// few real characters — U+0130 survives <c>ToLowerInvariant</c> unchanged but is folded by <c>lower()</c>.
    /// Rather than ask the database to imitate .NET, which it cannot, the aggregate only emits values both agree
    /// on: nothing in an uppercase or titlecase category, which is precisely what <c>lower()</c> changes, and no
    /// whitespace, which <c>btrim</c> only partly removes. The database can then hold the same rule, so two
    /// spellings of one recipient can never both occupy the pending slot.
    /// </para>
    /// </summary>
    private static string Normalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Invitation recipients cannot be empty.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant();
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

    /// <summary>The one hash format the project's token hasher emits: a version tag and a 256-bit Base64 digest.</summary>
    private const string HashVersion = "v1:";

    /// <summary>Base64 of a 32-byte digest is 44 characters, the last of which is always padding.</summary>
    private const int HashDigestLength = 44;

    /// <summary>
    /// Requires the exact format the token hasher produces, version and digest length included, rather than a
    /// loose shape. A usable token is 32 random bytes in Base64 and therefore the same length as a digest, so a
    /// length check alone would not separate them — but no token carries the version tag, and a value that is not
    /// a digest of the right size cannot be one either. Between this and the identical database constraint, the
    /// only value that can reach the column is something a hasher produced (IA-REQ-015/029).
    /// <para>
    /// Introducing a second hash version means changing this constant, the database constraint and a migration
    /// together; that coupling is deliberate, so a version can never be persisted that half the stack rejects.
    /// </para>
    /// </summary>
    private static string RequireVersionedHash(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || !IsVersionedHashShaped(tokenHash))
        {
            throw new ArgumentException("Invitation tokens are persisted only as a versioned hash.", nameof(tokenHash));
        }

        return tokenHash;
    }

    private static bool IsVersionedHashShaped(string tokenHash)
    {
        if (!tokenHash.StartsWith(HashVersion, StringComparison.Ordinal))
        {
            return false;
        }

        var digest = tokenHash.AsSpan(HashVersion.Length);
        if (digest.Length != HashDigestLength || digest[^1] != '=')
        {
            return false;
        }

        foreach (var character in digest[..^1])
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '+' && character != '/')
            {
                return false;
            }
        }

        return true;
    }
}
