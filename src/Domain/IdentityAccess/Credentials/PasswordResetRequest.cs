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

    public static PasswordResetRequest Issue(Guid identityId, VersionedTokenHash tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        if (tokenHash.IsEmpty) throw new ArgumentException("A reset request must carry a token hash.", nameof(tokenHash));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));

        return new PasswordResetRequest
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            TokenHash = tokenHash,
            Status = PasswordResetStatus.Pending,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime),
            Version = 1
        };
    }

    public bool IsPendingAt(DateTimeOffset now) => Status == PasswordResetStatus.Pending && now < ExpiresAt;

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
