using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// One pass of the delivery loop, invocable on its own. The hosted service that repeats it adds nothing but the
/// repetition, which is what lets a test drive exactly one iteration against a controlled clock instead of racing
/// a background poller (IA-REQ-028).
/// </summary>
public sealed class OutboxDispatcher(
    ApplicationDbContext context,
    IOutboxSecretReader secrets,
    IEnumerable<IOutboxDeliveryHandler> handlers,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private const int BatchSize = 32;

    /// <summary>
    /// Claims every due message and delivers each one once. Claiming is a single statement: the inner select
    /// takes row locks with <c>SKIP LOCKED</c> so competing workers pass over each other's rows instead of
    /// queueing, and the outer update is what actually grants the lease — a worker that reached the row second
    /// finds nothing left to claim rather than a lease it can overwrite.
    /// </summary>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var owner = $"{Environment.MachineName}:{Environment.CurrentManagedThreadId}:{Guid.NewGuid():N}";
        var claimed = await ClaimDueAsync(owner, now, cancellationToken);

        var delivered = 0;
        foreach (var id in claimed)
        {
            if (await DeliverAsync(id, now, cancellationToken))
            {
                delivered++;
            }
        }

        return delivered;
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueAsync(string owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages
            SET "LeaseOwner" = @owner, "LeaseExpiresAt" = @leaseUntil, "Generation" = "Generation" + 1
            WHERE "Id" IN (
                SELECT "Id" FROM outbox_messages
                WHERE "Status" = 'Pending'
                  AND "NextAttemptAt" <= @now
                  AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= @now)
                ORDER BY "NextAttemptAt"
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id";
            """;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
            {
                command.Transaction = transaction;
            }

            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("leaseUntil", now.Add(LeaseDuration));
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("batch", BatchSize);

            var ids = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }

            return ids;
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Delivers one claimed message. The token is decrypted into a local and never written anywhere; the outcome
    /// is one local transaction that settles the message and its envelope together, so a worker that dies between
    /// them cannot leave a delivered message whose envelope still holds a usable token (IA-REQ-018).
    /// </summary>
    private async Task<bool> DeliverAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        var secret = await context.OutboxSecrets.FirstOrDefaultAsync(candidate => candidate.OutboxMessageId == id, cancellationToken);
        if (message is null || secret is null)
        {
            return false;
        }

        // The claim was a raw statement, so a copy this context was already tracking still shows the row as it
        // was before the lease. Releasing the lease on that copy writes nothing — EF sees no change — and the row
        // would settle as terminal while still holding a lease, which the table's own constraint refuses.
        await context.Entry(message).ReloadAsync(cancellationToken);

        if (secret.Status != OutboxSecretStatus.Pending || secret.ExpiresAt <= now)
        {
            // The window closed before anyone delivered it. The message stops being work and the envelope is
            // terminalized, which is what clears the ciphertext without ever decrypting it.
            message.Abandon("envelope_expired", now);
            if (secret.Status == OutboxSecretStatus.Pending) secret.Terminate(OutboxSecretStatus.Expired, "envelope_expired", now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        var handler = handlers.FirstOrDefault(candidate => string.Equals(candidate.MessageType, message.Type, StringComparison.Ordinal));
        if (handler is null)
        {
            message.Fail("handler_missing", now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        var token = await secrets.ReadAsync(id, cancellationToken);
        if (token is null)
        {
            message.Fail("envelope_unreadable", now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        EmailDeliveryReceipt receipt;
        try
        {
            receipt = await handler.HandleAsync(id, message.Payload, token, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever the provider said stays out of the column. Its text is the most likely place for a token,
            // an address or a stack trace to end up persisted (IA-REQ-029).
            message.Fail("provider_error", now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (receipt.Delivered)
        {
            message.MarkDelivered(now);
            secret.MarkDelivered("delivered", now, receipt.ProviderReceipt ?? string.Empty);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (receipt.IsPermanentFailure)
        {
            message.Abandon("provider_rejected", now);
            secret.Terminate(OutboxSecretStatus.Failed, "provider_rejected", now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        message.Fail("provider_unavailable", now);
        if (message.Status == OutboxMessageStatus.Abandoned)
        {
            // The attempt budget ran out on this pass, so the envelope goes terminal with the message.
            secret.Terminate(OutboxSecretStatus.Failed, "attempts_exhausted", now);
        }

        await context.SaveChangesAsync(cancellationToken);
        return false;
    }
}

/// <summary>
/// A handler answers for one message type, so dispatch is a lookup rather than a chain of string comparisons
/// copied into every call site.
/// </summary>
public interface IOutboxDeliveryHandler
{
    string MessageType { get; }

    Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken);
}
