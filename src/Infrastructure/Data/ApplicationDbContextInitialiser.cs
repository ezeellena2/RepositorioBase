using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Data;

public sealed class ApplicationDbContextInitialiser(
    ApplicationDbContext context,
    PermissionCatalogSynchronizer permissionCatalogSynchronizer)
{
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        // This layer does not swallow the failure, so it does not own an Error record. The hosting boundary
        // decides whether startup can continue; logging here would duplicate that terminal observation.
        await context.Database.MigrateAsync(cancellationToken);
        await permissionCatalogSynchronizer.SynchronizeAsync(cancellationToken);
    }
}
