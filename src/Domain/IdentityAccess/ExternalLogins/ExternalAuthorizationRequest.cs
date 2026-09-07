using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Sessions;

namespace CleanArchitecture.Domain.IdentityAccess.ExternalLogins;

/// <summary>
/// What a provider round trip is for. The purposes live in server-side state and never cross: a challenge started
/// to sign in cannot come back and link, and one started to prove cannot come back and sign in (IA-REQ-052).
/// </summary>
public enum ExternalAuthorizationPurpose
{
    Login,
    Link,
    Proof
}

public enum ExternalAuthorizationStatus
{
    Started,
    Validated,
    Consumed,
    Failed
}

/// <summary>
/// One handoff to a provider and back.
/// <para>
/// It exists because the callback cannot be trusted to say what it is for. The middleware validates the protocol —
/// state, nonce, PKCE, issuer, audience, signature, expiry — and this row carries the only thing the protocol does
/// not: which of this system's operations the person started, and on whose behalf. A callback that arrives without
/// a live record of its own matches nothing.
/// </para>
/// </summary>
public sealed class ExternalAuthorizationRequest : BaseEntity<Guid>
{
    private ExternalAuthorizationRequest() { }

    public string Provider { get; private set; } = string.Empty;

    public ExternalAuthorizationPurpose Purpose { get; private set; }

    /// <summary>Absent for a sign-in: nobody is signed in yet, which is the point of it.</summary>
    public Guid? IdentityId { get; private set; }

    public UserSessionId? SessionId { get; private set; }

    /// <summary>The action a `Proof` will authorize. Absent for the other purposes.</summary>
    public string? Action { get; private set; }

    public ExternalAuthorizationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>The provider's own identifier for the person, written only after the protocol validated it.</summary>
    public string? Subject { get; private set; }

    public string? ProviderEmail { get; private set; }

    public bool EmailVerified { get; private set; }

    public int Version { get; private set; }

    public static ExternalAuthorizationRequest Start(
        string provider,
        ExternalAuthorizationPurpose purpose,
        Guid? identityId,
        UserSessionId? sessionId,
        string? action,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (purpose != ExternalAuthorizationPurpose.Login && (identityId is null || identityId == Guid.Empty))
        {
            throw new ArgumentException("Only a sign-in may start without an identity.", nameof(identityId));
        }

        if (purpose == ExternalAuthorizationPurpose.Proof && string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("A proof handoff names the action it will authorize.", nameof(action));
        }

        return new ExternalAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Purpose = purpose,
            IdentityId = identityId,
            SessionId = sessionId,
            Action = action,
            Status = ExternalAuthorizationStatus.Started,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            Version = 1
        };
    }

    public bool IsLiveAt(DateTimeOffset now) => Status == ExternalAuthorizationStatus.Started && now < ExpiresAt;

    public bool IsValidatedAt(DateTimeOffset now) => Status == ExternalAuthorizationStatus.Validated && now < ExpiresAt;

    /// <summary>
    /// Records what the protocol proved. Only the callback calls this, and only once.
    /// <para>
    /// <paramref name="notAfter"/> is the moment the evidence behind this handoff stops being fresh. It can only
    /// bring the window in, never push it out: a handoff is dead when either its own lifetime or the
    /// authentication it rests on has run out, whichever happens first (IA-REQ-051).
    /// </para>
    /// </summary>
    public void Validated(string subject, string? providerEmail, bool emailVerified, DateTimeOffset now, DateTimeOffset? notAfter = null)
    {
        if (Status != ExternalAuthorizationStatus.Started) throw new InvalidOperationException("Only a started handoff can be validated.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = subject;
        ProviderEmail = providerEmail;
        EmailVerified = emailVerified;
        if (notAfter is { } deadline && deadline < ExpiresAt) ExpiresAt = deadline;
        Status = ExternalAuthorizationStatus.Validated;
        Version++;
    }

    public void Consume(DateTimeOffset now) => Settle(ExternalAuthorizationStatus.Consumed, now);

    public void Fail(DateTimeOffset now) => Settle(ExternalAuthorizationStatus.Failed, now);

    private void Settle(ExternalAuthorizationStatus status, DateTimeOffset now)
    {
        if (Status is ExternalAuthorizationStatus.Consumed or ExternalAuthorizationStatus.Failed)
        {
            throw new InvalidOperationException("A settled handoff cannot be settled again.");
        }

        Status = status;
        SettledAt = now;
        Version++;
    }
}
