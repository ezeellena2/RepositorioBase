using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
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
            await transaction.CommitAsync(cancellationToken);
        });
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
