using CleanArchitecture.Domain.IdentityAccess.Outbox;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;

public interface IConfirmationSecretStore
{
    Task<OutboxSecret?> GetByVersionedHashForUpdateAsync(string versionedHash, CancellationToken cancellationToken);
}
