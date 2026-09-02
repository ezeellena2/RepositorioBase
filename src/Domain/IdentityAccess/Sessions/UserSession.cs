using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Sessions;

public sealed class UserSession : BaseEntity<UserSessionId>
{
    private UserSession() { }

    public Guid IdentityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset IdleExpiresAt { get; private set; }
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public TenantId? ActiveTenantId { get; private set; }
    public int Version { get; private set; }

    public static UserSession Create(Guid identityId, DateTimeOffset createdAt, TimeSpan idleLifetime, TimeSpan absoluteLifetime)
    {
        if (identityId == Guid.Empty) throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        if (idleLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleLifetime));
        if (absoluteLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(absoluteLifetime));

        var absoluteExpiresAt = createdAt.Add(absoluteLifetime);
        return new UserSession
        {
            Id = UserSessionId.New(),
            IdentityId = identityId,
            CreatedAt = createdAt,
            LastSeenAt = createdAt,
            IdleExpiresAt = Min(createdAt.Add(idleLifetime), absoluteExpiresAt),
            AbsoluteExpiresAt = absoluteExpiresAt,
            Version = 1
        };
    }

    public bool IsActiveAt(DateTimeOffset now) => now >= CreatedAt && RevokedAt is null && now < IdleExpiresAt && now < AbsoluteExpiresAt;

    /// <summary>
    /// Records a validated use of the session. Timestamps only move forward and the concurrency token is
    /// untouched: activity tracking is not a state transition, so parallel requests of one session never
    /// conflict with each other. Persistence must still verify liveness atomically against the stored row.
    /// </summary>
    public void Touch(DateTimeOffset now)
    {
        EnsureActive(now);
        if (now <= LastSeenAt) return;

        var idleLifetime = IdleExpiresAt - LastSeenAt;
        LastSeenAt = now;
        IdleExpiresAt = Min(now.Add(idleLifetime), AbsoluteExpiresAt);
    }

    public void SelectTenant(TenantId tenantId, DateTimeOffset now)
    {
        if (tenantId.IsEmpty) throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(tenantId));
        EnsureActive(now);
        if (ActiveTenantId == tenantId) return;

        ActiveTenantId = tenantId;
        Version++;
    }

    public void ClearActiveTenant(DateTimeOffset now)
    {
        EnsureActive(now);
        if (ActiveTenantId is null) return;
        ActiveTenantId = null;
        Version++;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (now < CreatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (RevokedAt is not null) return;
        RevokedAt = now;
        ActiveTenantId = null;
        Version++;
    }

    private void EnsureActive(DateTimeOffset now)
    {
        if (!IsActiveAt(now)) throw new InvalidOperationException("Only active sessions can be changed.");
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}
