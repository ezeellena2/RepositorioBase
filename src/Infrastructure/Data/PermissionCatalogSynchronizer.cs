using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Data;

public sealed class PermissionCatalogSynchronizer(ApplicationDbContext context)
{
    private const long CatalogSynchronizationLockId = 3_711_804_233_991L;

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CatalogSynchronizationLockId});", cancellationToken);
            var existing = await context.Permissions
                .AsNoTracking()
                .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);
            ValidateCatalog(existing);

            foreach (var definition in Permissions.Catalog.OrderBy(permission => permission.Code, StringComparer.Ordinal))
            {
                if (!existing.ContainsKey(definition.Code))
                {
                    context.Permissions.Add(Permission.Create(definition.Code, definition.AllowedTenantTypes));
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            var synchronized = await context.Permissions
                .AsNoTracking()
                .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);
            ValidateCatalog(synchronized);
            await BackfillOrganizationOwnersAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// Gives every `Organization`'s system `Owner` role the codes the catalogue says it holds (amendment D1).
    /// <para>
    /// It runs here because this is already the one place the code-owned catalogue is written to the database, and
    /// because the repair is otherwise impossible from inside the product: C5's ceiling lets an actor grant only
    /// what it holds, so an owner provisioned with nothing — which is every organization registered before this —
    /// can never grant itself anything. The pass is idempotent and touches nothing else: not a custom role that
    /// happens to be called Owner, not `Personal`, not `Platform`, and no code the decision withholds.
    /// </para>
    /// </summary>
    private async Task BackfillOrganizationOwnersAsync(CancellationToken cancellationToken)
    {
        var codes = Permissions.OrganizationOwnerCodes;
        var owners = await (
            from tenant in context.Tenants
            join role in context.TenantRoles on tenant.Id equals role.TenantId
            where tenant.Type == TenantType.Organization && role.IsSystem && !role.IsRetired
            select new { Tenant = tenant, Role = role }).ToListAsync(cancellationToken);
        if (owners.Count == 0) return;

        var roleIds = owners.Select(owner => owner.Role.Id).ToArray();
        var held = (await context.RolePermissions
                .AsNoTracking()
                .Where(permission => roleIds.Contains(permission.RoleId))
                .Select(permission => new { permission.RoleId, permission.PermissionCode })
                .ToListAsync(cancellationToken))
            .ToLookup(permission => permission.RoleId, permission => permission.PermissionCode, EqualityComparer<RoleId>.Default);

        var catalogue = await context.Permissions
            .Where(permission => codes.Contains(permission.Code))
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        var added = false;
        foreach (var owner in owners)
        {
            var missing = codes.Except(held[owner.Role.Id], StringComparer.Ordinal);
            foreach (var code in missing)
            {
                if (!catalogue.TryGetValue(code, out var permission)) continue;
                context.RolePermissions.Add(RolePermission.Create(owner.Tenant, owner.Role, permission));
                added = true;
            }
        }

        // The authorization version each grant advanced has to reach the database with the grants themselves, and
        // the audit interceptor writes its `role.changed` rows from the same save.
        if (added) await context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateCatalog(IReadOnlyDictionary<string, Permission> existing)
    {
        var catalogCodes = Permissions.Catalog.Select(definition => definition.Code).ToHashSet(StringComparer.Ordinal);
        var staleCodes = existing.Keys.Where(code => !catalogCodes.Contains(code)).Order(StringComparer.Ordinal).ToArray();
        if (staleCodes.Length > 0)
        {
            throw new InvalidOperationException($"Persisted permissions are not present in the current catalog: {string.Join(", ", staleCodes)}.");
        }

        foreach (var definition in Permissions.Catalog.OrderBy(permission => permission.Code, StringComparer.Ordinal))
        {
            if (existing.TryGetValue(definition.Code, out var persisted) && !persisted.AllowedTenantTypes.SetEquals(definition.AllowedTenantTypes))
            {
                throw new InvalidOperationException($"Permission catalog entry '{definition.Code}' has incompatible tenant-type scope.");
            }
        }
    }
}
