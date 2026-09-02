using System.Data.Common;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Races the persistence of a failed sign-in attempt: it can hold every attempt at a barrier until the armed
/// number of them contend for the same account row, and it can commit competing failures of that account just
/// before the statement runs. It matches the statement rather than the mechanism, so it exercises a read-modify-
/// write and an atomic conditional update alike.
/// </summary>
public sealed class FailedAccessBarrierInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RaceAsync(command, eventData, cancellationToken);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RaceAsync(command, eventData, cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static async Task RaceAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (!IsFailedAccessWrite(command.CommandText)) return;

        await TestApp.WaitForFailedAccessBarrierAsync(cancellationToken);

        var connectionString = eventData.Context?.Database.GetConnectionString();
        if (connectionString is null || !TestApp.HasPendingConcurrentFailedAccess) return;

        var identityId = ExtractUserId(command);
        while (TestApp.ConsumeConcurrentFailedAccess())
        {
            // A competing failure of the same account, committed on its own connection, exactly like a second
            // real request: it counts an attempt and rotates the Identity concurrency stamp.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
            await using var competingContext = new ApplicationDbContext(options);
            var competingUser = await competingContext.Users.SingleAsync(user => user.Id == identityId, cancellationToken);
            competingUser.AccessFailedCount++;
            competingUser.ConcurrencyStamp = Guid.NewGuid().ToString();
            await competingContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The account identifier is the statement's only <see cref="Guid"/> parameter. Requiring exactly one makes
    /// the hook fail loudly instead of silently racing the wrong account if the statement ever gains another.
    /// </summary>
    private static Guid ExtractUserId(DbCommand command)
    {
        var identifiers = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<Guid>().ToArray();
        return identifiers.Length == 1
            ? identifiers[0]
            : throw new InvalidOperationException($"A failed-access statement must carry exactly one account identifier; found {identifiers.Length}.");
    }

    /// <summary>
    /// Matches the failed-access statement and nothing else: only it assigns <c>LockoutEnd</c>. The reset issued
    /// by a successful sign-in touches <c>AccessFailedCount</c> too, and must never be raced or held at a barrier.
    /// </summary>
    private static bool IsFailedAccessWrite(string commandText) =>
        commandText.Contains("UPDATE \"AspNetUsers\"", StringComparison.Ordinal) &&
        commandText.Contains("AccessFailedCount", StringComparison.Ordinal) &&
        commandText.Contains("LockoutEnd", StringComparison.Ordinal);
}
