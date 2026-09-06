using System.Buffers.Binary;
using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// The per-identity session lock (IA-REQ-049).
/// <para>
/// It is the two-argument advisory lock space, which PostgreSQL keeps entirely separate from the one-argument space
/// registration already uses. Without that separation a sign-in and a registration for one person could block each
/// other for no reason, or worse, appear to be serialized when they were not.
/// </para>
/// <para>
/// The wait is bounded because holding a request open on a lock is its own denial of service. When it elapses the
/// caller answers `429`, a status a wrong password can also produce, rather than one only a valid credential could.
/// </para>
/// </summary>
public sealed class SessionLock(ApplicationDbContext context) : ISessionLock
{
    /// <summary>The first argument, constant for every session lock, is what makes this a space of its own.</summary>
    private const int LockSpace = 0x5E5510;

    /// <summary>How long a sign-in waits for another one to finish. A **product default**.</summary>
    private const string LockTimeout = "3s";

    public async Task<bool> TryAcquireAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var key = Key(identityId);
        try
        {
            // LOCAL, so it lapses with the transaction rather than leaking onto the pooled connection.
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL lock_timeout = '{LockTimeout}';", cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockSpace}, {key});", cancellationToken);
            return true;
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return false;
        }
    }

    /// <summary>
    /// A stable 32-bit key for the identity. A hash rather than the raw bits so the lock space is spread evenly,
    /// and collisions only ever cost two unrelated identities a short wait.
    /// </summary>
    private static int Key(Guid identityId) =>
        BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(identityId.ToByteArray()).AsSpan(0, sizeof(int)));
}
