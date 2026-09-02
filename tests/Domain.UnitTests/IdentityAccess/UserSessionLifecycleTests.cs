using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class UserSessionLifecycleTests
{
    [Test]
    public void Create_binds_a_non_empty_session_to_an_identity_and_calculates_both_expirations()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var identityId = Guid.NewGuid();

        var session = UserSession.Create(identityId, createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

        session.Id.IsEmpty.ShouldBeFalse();
        session.IdentityId.ShouldBe(identityId);
        session.CreatedAt.ShouldBe(createdAt);
        session.LastSeenAt.ShouldBe(createdAt);
        session.IdleExpiresAt.ShouldBe(createdAt.AddMinutes(30));
        session.AbsoluteExpiresAt.ShouldBe(createdAt.AddHours(12));
        session.IsActiveAt(createdAt.AddMinutes(29)).ShouldBeTrue();
        session.IsActiveAt(createdAt.AddHours(12)).ShouldBeFalse();
    }

    [Test]
    public void Revoke_is_idempotent_terminal_and_clears_the_active_tenant()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var tenant = TenantId.New();
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        session.SelectTenant(tenant, createdAt);

        session.Revoke(createdAt.AddMinutes(1));
        var versionAfterFirstRevoke = session.Version;
        session.Revoke(createdAt.AddMinutes(2));

        session.RevokedAt.ShouldBe(createdAt.AddMinutes(1));
        session.ActiveTenantId.ShouldBeNull();
        session.IsActiveAt(createdAt.AddMinutes(2)).ShouldBeFalse();
        session.Version.ShouldBe(versionAfterFirstRevoke);
        Should.Throw<InvalidOperationException>(() => session.SelectTenant(tenant, createdAt.AddMinutes(2)));
    }

    [Test]
    public void Touch_extends_idle_expiry_without_extending_absolute_expiry_or_accepting_expired_time()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

        session.Touch(createdAt.AddMinutes(20));

        session.LastSeenAt.ShouldBe(createdAt.AddMinutes(20));
        session.IdleExpiresAt.ShouldBe(createdAt.AddMinutes(50));
        session.AbsoluteExpiresAt.ShouldBe(createdAt.AddHours(12));
        Should.Throw<InvalidOperationException>(() => session.Touch(createdAt.AddHours(12)));
    }

    [Test]
    public void Tenant_mutations_require_an_explicit_current_active_instant_without_touching_last_seen()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var tenant = TenantId.New();
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));

        session.SelectTenant(tenant, createdAt.AddMinutes(1));
        session.LastSeenAt.ShouldBe(createdAt);
        session.ClearActiveTenant(createdAt.AddMinutes(2));
        session.ActiveTenantId.ShouldBeNull();

        Should.Throw<InvalidOperationException>(() => session.SelectTenant(tenant, createdAt.AddMinutes(30)));
        Should.Throw<InvalidOperationException>(() => session.ClearActiveTenant(createdAt.AddMinutes(30)));
        Should.Throw<InvalidOperationException>(() => session.SelectTenant(tenant, createdAt.AddHours(1)));
        Should.Throw<InvalidOperationException>(() => session.ClearActiveTenant(createdAt.AddHours(1)));
    }

    [Test]
    public void Revoke_rejects_time_before_creation_and_remains_idempotent_after_valid_revoke()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

        Should.Throw<ArgumentOutOfRangeException>(() => session.Revoke(createdAt.AddTicks(-1)));
        session.Revoke(createdAt);
        session.Revoke(createdAt.AddMinutes(1));

        session.RevokedAt.ShouldBe(createdAt);
    }

    [Test]
    public void Pre_creation_instant_is_inactive_and_cannot_mutate_tenant_context()
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var selectedTenant = TenantId.New();
        var attemptedTenant = TenantId.New();
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        session.SelectTenant(selectedTenant, createdAt);
        var version = session.Version;

        session.IsActiveAt(createdAt.AddTicks(-1)).ShouldBeFalse();
        session.IsActiveAt(createdAt).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => session.SelectTenant(attemptedTenant, createdAt.AddTicks(-1)));
        Should.Throw<InvalidOperationException>(() => session.ClearActiveTenant(createdAt.AddTicks(-1)));

        session.ActiveTenantId.ShouldBe(selectedTenant);
        session.Version.ShouldBe(version);
    }

    [TestCase(10)]
    [TestCase(0)]
    [TestCase(-5)]
    public void Touch_never_changes_the_concurrency_token_and_only_moves_timestamps_forward(int minutesRelativeToLastSeen)
    {
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var session = UserSession.Create(Guid.NewGuid(), createdAt, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        session.SelectTenant(TenantId.New(), createdAt);
        session.Touch(createdAt.AddMinutes(10));
        var lastSeenAt = session.LastSeenAt;
        var idleExpiresAt = session.IdleExpiresAt;
        var version = session.Version;
        var expectedLastSeenAt = minutesRelativeToLastSeen > 0 ? lastSeenAt.AddMinutes(minutesRelativeToLastSeen) : lastSeenAt;

        session.Touch(lastSeenAt.AddMinutes(minutesRelativeToLastSeen));

        session.LastSeenAt.ShouldBe(expectedLastSeenAt);
        session.IdleExpiresAt.ShouldBe(minutesRelativeToLastSeen > 0 ? idleExpiresAt.AddMinutes(minutesRelativeToLastSeen) : idleExpiresAt);
        session.AbsoluteExpiresAt.ShouldBe(createdAt.AddHours(12));
        session.Version.ShouldBe(version);
        version.ShouldBe(2);
    }
}
