using System.Buffers.Binary;
using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// A fourth advisory space, after the one-argument registration space, the session space and the subject space.
/// <para>
/// It is held to commit, which is the only lifetime that helps here: what the two sides need is not to take turns
/// reading, but for one of them to be unable to commit while the other's rows are still invisible.
/// </para>
/// <para>
/// Deliberately without <c>SET LOCAL lock_timeout</c>, unlike the two locks it is modelled on. `SET LOCAL` scopes
/// to the whole transaction, so it would also bound the row waits that a widening's own offer-cancellation takes
/// afterwards, turning a wait that is correct today into an unmapped `55P03`. The wait here is bounded by shape
/// instead: the only transaction that waits is the one holding nothing.
/// </para>
/// </summary>
public sealed class RoleAuthorityLock(ApplicationDbContext context) : IRoleAuthorityLock
{
    /// <summary>The first argument, constant for every role-authority lock, is what makes this a space of its own.</summary>
    private const int LockSpace = 0x5E5512;

    public Task AcquireAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({LockSpace}, {Key(tenantId)});", cancellationToken);

    public Task<bool> TryAcquireAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockSpace}, {Key(tenantId)}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    /// <summary>
    /// A tenant identifier is 128 bits and an advisory key is 32, so it is hashed rather than truncated: two
    /// tenants sharing a key would only ever wait for each other, never confuse each other's rows.
    /// </summary>
    private static int Key(TenantId tenantId) =>
        BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(tenantId.Value.ToByteArray()).AsSpan(0, sizeof(int)));
}
