using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// The per-subject lock that makes "one provider account belongs to one identity" decidable by reading.
/// <para>
/// The session lock is keyed on the identity, which serializes everything one person does. It cannot serialize
/// this: two <em>different</em> identities racing for the same provider account take two different identity
/// locks and both read a subject nobody owns. The database's own unique key still refuses the second insert —
/// but it refuses it by throwing, inside a transaction that is then aborted and cannot even record the refusal,
/// so the loser meets a `500` instead of the conflict the contract names.
/// </para>
/// <para>
/// Locking the subject instead makes the read authoritative: the loser waits, sees the owner, and answers
/// `external_login_conflict` through the ordinary path. It is a third advisory space, distinct from the
/// one-argument registration space and the two-argument session space.
/// </para>
/// </summary>
public sealed class ExternalSubjectLock(ApplicationDbContext context) : IExternalSubjectLock
{
    /// <summary>The first argument, constant for every subject lock, is what makes this a space of its own.</summary>
    private const int LockSpace = 0x5E5511;

    /// <summary>How long one round trip waits for another to finish with the same provider account.</summary>
    private const string LockTimeout = "3s";

    public async Task<bool> TryAcquireAsync(string provider, string subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject)) return false;

        var key = Key(provider, subject);
        try
        {
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
    /// A stable 32-bit key for the provider account. The separator is a character no provider or subject
    /// contains, so two of them cannot collide by concatenation, and it is hashed rather than used raw because
    /// the key space is only 32 bits wide.
    /// </summary>
    private static int Key(string provider, string subject) =>
        BinaryPrimitives.ReadInt32BigEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes(provider + '\n' + subject)).AsSpan(0, sizeof(int)));
}
