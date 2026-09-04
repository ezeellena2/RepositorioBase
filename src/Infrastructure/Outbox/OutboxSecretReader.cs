using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// Reads and decrypts one envelope. It uses the same data-protection purpose the writer used, because a
/// different one cannot unprotect existing ciphertext.
/// </summary>
public sealed class OutboxSecretReader(ApplicationDbContext context) : IOutboxSecretReader
{
    private readonly ApplicationDbContext _context = context;

    public Task<string?> ReadAsync(Guid outboxMessageId, CancellationToken cancellationToken) => throw new NotImplementedException();
}
