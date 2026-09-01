using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class ConfirmationSecretStore(ApplicationDbContext context) : IConfirmationSecretStore
{
    public Task<OutboxSecret?> GetByVersionedHashForUpdateAsync(string versionedHash, CancellationToken cancellationToken) =>
        context.OutboxSecrets
            .FromSqlInterpolated($"SELECT * FROM outbox_secrets WHERE \"VersionedHash\" = {versionedHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
