using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.IdentityAccess.Sessions;

/// <summary>How the person proved it was still them.</summary>
public enum RecentIdentityProofMethod
{
    Password,
    ExternalProvider
}

/// <summary>
/// A single-use record that a sensitive change may spend once (IA-REQ-051).
/// <para>
/// It binds four things at once, and all four matter. One identity, so it cannot be borrowed. One session, so a
/// proof made on a laptop cannot authorize a change made from somewhere else. One action, so proving in order to
/// unlink a provider does not also authorize changing a password. And the identity's security version at the
/// moment it was issued, so anything that changes a credential invalidates every proof outstanding against it.
/// </para>
/// <para>
/// It never travels to the client. The request that spends it looks up the live unconsumed record for
/// `(identity, current session, action)`; there is nothing for a caller to hold, copy or replay.
/// </para>
/// </summary>
public sealed class RecentIdentityProof : BaseEntity<Guid>
{
    private RecentIdentityProof() { }

    public Guid IdentityId { get; private set; }

    public UserSessionId SessionId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public RecentIdentityProofMethod Method { get; private set; }

    public long SecurityVersion { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? ConsumedReason { get; private set; }

    public int Version { get; private set; }

    public static RecentIdentityProof Issue(
        Guid identityId,
        UserSessionId sessionId,
        string action,
        RecentIdentityProofMethod method,
        long securityVersion,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        if (sessionId.IsEmpty) throw new ArgumentException("Session identifiers cannot be empty.", nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        if (securityVersion < 0) throw new ArgumentOutOfRangeException(nameof(securityVersion));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));

        return new RecentIdentityProof
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            SessionId = sessionId,
            Action = action,
            Method = method,
            SecurityVersion = securityVersion,
            IssuedAt = now,
            ExpiresAt = now.Add(lifetime),
            Version = 1
        };
    }

    public bool IsLiveAt(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    public void Consume(string reason, DateTimeOffset now)
    {
        if (ConsumedAt is not null) throw new InvalidOperationException("A proof cannot be spent twice.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ConsumedAt = now;
        ConsumedReason = reason;
        Version++;
    }
}

/// <summary>
/// The identity's security version, owned by the domain rather than by ASP.NET Identity's `SecurityStamp`.
/// <para>
/// It is a separate row and not a column on `ApplicationUser` for the reason the whole identity boundary exists:
/// the application layer must be able to reason about "has anything about this person's credentials changed" without
/// reaching into the Identity schema. An absent row means version zero, so nothing needs backfilling.
/// </para>
/// </summary>
public sealed class IdentitySecurityState
{
    private IdentitySecurityState() { }

    public Guid IdentityId { get; private set; }

    public long SecurityVersion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public static IdentitySecurityState Start(Guid identityId, DateTimeOffset now)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        return new IdentitySecurityState { IdentityId = identityId, SecurityVersion = 0, UpdatedAt = now, Version = 1 };
    }

    public void Advance(DateTimeOffset now)
    {
        SecurityVersion++;
        UpdatedAt = now;
        Version++;
    }
}
