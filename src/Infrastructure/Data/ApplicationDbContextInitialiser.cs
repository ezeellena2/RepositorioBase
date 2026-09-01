using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Data;

public sealed class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly PermissionCatalogSynchronizer _permissionCatalogSynchronizer;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context,
        PermissionCatalogSynchronizer permissionCatalogSynchronizer)
    {
        _logger = logger;
        _context = context;
        _permissionCatalogSynchronizer = permissionCatalogSynchronizer;
    }

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Database.MigrateAsync(cancellationToken);
            await _permissionCatalogSynchronizer.SynchronizeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }
}
