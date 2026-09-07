using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Security;

namespace CleanArchitecture.Domain.IdentityAccess.Credentials;

public enum PasswordResetStatus
{
    Pending,
    Consumed,
    Superseded
}

/// <summary>
/// One outstanding "I forgot my password" (IA-REQ-051's recovery half).
/// <para>
/// Only the hash of the mailed token is here; the usable token lives, encrypted and expiring, in the
/// <c>OutboxSecret</c> the delivery reads. An identity holds at most one pending request, so asking again replaces
/// the previous link rather than leaving two that work — the second live link is the one nobody remembers sending.
/// </para>
/// </summary>
public sealed class PasswordResetRequest : BaseEntity<Guid>
{
    private PasswordResetRequest() { }

    public Guid IdentityId { get; private set; }

    public VersionedTokenHash TokenHash { get; private set; }

    public PasswordResetStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public int Version { get; private set; }

    /// <summary>
    /// The identity's security version when this link was minted. A link is a statement about the credential as
    /// it stood at that moment, so anything that replaces the credential — another reset, an authenticated
    /// change, a provider link — moves the version past it and this link stops being about anything real. Zero
    /// is the correct value for an identity that has no security state yet (IA-REQ-051).
    /// </summary>
    public long SecurityVersion { get; private set; }

    public static PasswordResetRequest Issue(
        Guid identityId, VersionedTokenHash tokenHash, DateTimeOffset now, TimeSpan lifetime, long securityVersion = 0)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        if (tokenHash.IsEmpty) throw new ArgumentException("A reset request must carry a token hash.", nameof(tokenHash));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (securityVersion < 0) throw new ArgumentOutOfRangeException(nameof(securityVersion));

        return new PasswordResetRequest
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            TokenHash = tokenHash,
            Status = PasswordResetStatus.Pending,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime),
            SecurityVersion = securityVersion,
            Version = 1
        };
    }

    public bool IsPendingAt(DateTimeOffset now) => Status == PasswordResetStatus.Pending && now < ExpiresAt;

    /// <summary>
    /// Usable only while it is still about the credential it was minted for. A link outlives its own window, its
    /// own spending and its own supersession — and it must also not outlive the credential, or a link mailed
    /// before an authenticated change would silently undo that change (IA-REQ-051, C4).
    /// </summary>
    public bool IsPendingAt(DateTimeOffset now, long securityVersion) =>
        IsPendingAt(now) && SecurityVersion == securityVersion;

    public void Consume(DateTimeOffset now) => Settle(PasswordResetStatus.Consumed, now);

    public void Supersede(DateTimeOffset now) => Settle(PasswordResetStatus.Superseded, now);

    private void Settle(PasswordResetStatus status, DateTimeOffset now)
    {
        if (Status != PasswordResetStatus.Pending) throw new InvalidOperationException("A settled reset request cannot be settled again.");
        Status = status;
        SettledAt = now;
        Version++;
    }
}
