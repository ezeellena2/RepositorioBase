using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// Reads and decrypts one envelope into the caller's memory. It shares the writer's data-protection purpose
/// because a different one cannot unprotect existing ciphertext, and it exposes no listing member: a reader that
/// could enumerate envelopes would be a way to harvest tokens rather than to deliver one (IA-REQ-018).
/// </summary>
public sealed class OutboxSecretReader(ApplicationDbContext context, IDataProtectionProvider dataProtectionProvider) : IOutboxSecretReader
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("identity-access.registration.outbox-secret.v1");

    public async Task<string?> ReadAsync(Guid outboxMessageId, CancellationToken cancellationToken)
    {
        var ciphertext = await context.OutboxSecrets
            .AsNoTracking()
            .Where(secret => secret.OutboxMessageId == outboxMessageId)
            .Select(secret => secret.Ciphertext)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(ciphertext))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // A payload this key cannot open is not an unexpected failure: it is an envelope written under a
            // retired key, and the caller answers for it the way it answers for any undeliverable one.
            return null;
        }
    }
}
