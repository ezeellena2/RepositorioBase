using System.Diagnostics;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Infrastructure.Data.Interceptors;

public sealed class TenantAuthorizationAuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendAuditEvents(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AppendAuditEvents(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void AppendAuditEvents(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var changes = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        RejectSystemRoleDeletion(changes);
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        foreach (var change in changes.Where(change => change.Entity is TenantMembership or MembershipRole))
        {
            var tenantId = GetTenantId(change.Entity);
            if (RequiresPersistenceVersionIncrement(change))
            {
                EnsureAuthorizationVersion(context, tenantId);
            }
            context.Add(AuditEvent.CreateMembershipChanged(tenantId, null, correlationId, GetOutcome(change)));
        }

        foreach (var change in changes.Where(change => change.Entity is Role or RolePermission))
        {
            var tenantId = GetTenantId(change.Entity);
            if (RequiresPersistenceVersionIncrement(change))
            {
                EnsureAuthorizationVersion(context, tenantId);
            }

            context.Add(AuditEvent.CreateRoleChanged(tenantId, null, correlationId, GetOutcome(change)));
        }
    }

    private static void RejectSystemRoleDeletion(IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> changes)
    {
        if (changes.Any(change => change.State == EntityState.Deleted && change.Entity is Role { IsSystem: true }))
        {
            throw new InvalidOperationException("System roles cannot be deleted.");
        }
    }

    private static bool RequiresPersistenceVersionIncrement(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry change) =>
        ((change.State is EntityState.Added or EntityState.Deleted) && change.Entity is Role or RolePermission or MembershipRole) ||
        (change.State == EntityState.Deleted && change.Entity is TenantMembership) ||
        (change.Entity is TenantMembership && change.State == EntityState.Modified) ||
        (change.Entity is Role && change.State == EntityState.Modified &&
            (change.Property(nameof(Role.Name)).IsModified || change.Property(nameof(Role.NormalizedName)).IsModified || change.Property(nameof(Role.IsRetired)).IsModified));

    private static TenantId GetTenantId(object entity) => entity switch
    {
        TenantMembership membership => membership.TenantId,
        MembershipRole membershipRole => membershipRole.TenantId,
        Role role => role.TenantId,
        RolePermission rolePermission => rolePermission.TenantId,
        _ => throw new InvalidOperationException("Unsupported authorization entity.")
    };

    private static string GetOutcome(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry change) => change.State switch
    {
        EntityState.Added when change.Entity is RolePermission or MembershipRole => "granted",
        EntityState.Deleted when change.Entity is RolePermission or MembershipRole => "revoked",
        EntityState.Deleted => "deleted",
        EntityState.Modified when change.Entity is Role && change.Property(nameof(Role.IsRetired)).IsModified => "retired",
        EntityState.Modified => "changed",
        _ => "changed"
    };

    private static void EnsureAuthorizationVersion(DbContext context, TenantId tenantId)
    {
        var tracked = context.ChangeTracker.Entries<Tenant>().SingleOrDefault(entry => entry.Entity.Id == tenantId);
        if (tracked is not null && (tracked.State == EntityState.Added || tracked.Property(nameof(Tenant.AuthorizationVersion)).IsModified))
        {
            return;
        }

        var tenant = tracked?.Entity ?? context.Set<Tenant>().Single(candidate => candidate.Id == tenantId);
        tenant.IncrementAuthorizationVersion();
    }
}
