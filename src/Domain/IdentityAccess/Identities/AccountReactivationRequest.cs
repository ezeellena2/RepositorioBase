using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Security;

namespace CleanArchitecture.Domain.IdentityAccess.Identities;

public enum AccountReactivationStatus
{
    Pending,
    Consumed,
    Superseded
}

/// <summary>
/// One outstanding "I would like my account back" (IA-REQ-054).
/// <para>
/// Only the hash of the mailed ticket is here; the usable ticket lives, encrypted and expiring, in the
/// <c>OutboxSecret</c> the delivery reads. An identity holds at most one pending ticket, so asking again replaces
/// the previous one rather than leaving two that work.
/// </para>
/// <para>
/// It deliberately carries no security version, unlike <c>PasswordResetRequest</c>. That stamp exists because a
/// reset <em>replaces</em> a credential, so a link minted before an authenticated change would silently undo it.
/// A ticket replaces nothing: it is spent alongside the password as it stands at that moment, checked live. A
/// stamp here would refuse the person who did exactly what withdrawal E1 tells a provider-only identity to do —
/// set a password first, then come back — which is the one path E1 leaves them.
/// </para>
/// </summary>
public sealed class AccountReactivationRequest : BaseEntity<Guid>
{
    private AccountReactivationRequest() { }

    public Guid IdentityId { get; private set; }

    public VersionedTokenHash TokenHash { get; private set; }

    public AccountReactivationStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public int Version { get; private set; }

    public static AccountReactivationRequest Issue(Guid identityId, VersionedTokenHash tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        if (tokenHash.IsEmpty) throw new ArgumentException("A reactivation request must carry a token hash.", nameof(tokenHash));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));

        return new AccountReactivationRequest
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            TokenHash = tokenHash,
            Status = AccountReactivationStatus.Pending,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime),
            Version = 1
        };
    }

    public bool IsPendingAt(DateTimeOffset now) => Status == AccountReactivationStatus.Pending && now < ExpiresAt;

    public void Consume(DateTimeOffset now) => Settle(AccountReactivationStatus.Consumed, now);

    public void Supersede(DateTimeOffset now) => Settle(AccountReactivationStatus.Superseded, now);

    private void Settle(AccountReactivationStatus status, DateTimeOffset now)
    {
        if (Status != AccountReactivationStatus.Pending) throw new InvalidOperationException("A settled reactivation request cannot be settled again.");
        Status = status;
        SettledAt = now;
        Version++;
    }
}
