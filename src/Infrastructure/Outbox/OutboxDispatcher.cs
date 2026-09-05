using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>Claims immediately before each send; external uncertainty is reconciled by the provider's stable key.</summary>
public sealed class OutboxDispatcher(
    ApplicationDbContext context,
    IOutboxSecretReader secrets,
    IEnumerable<IOutboxDeliveryHandler> handlers,
    TimeProvider timeProvider,
    IIdentityEmailSender sender)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReceiptRetention = TimeSpan.FromHours(24);
    private const int BatchSize = 32;
    private const int MaxAttempts = 8;

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        sender.ValidateConfiguration(); // A bad deployment must not claim messages or spend attempts.
        var delivered = 0;
        for (var index = 0; index < BatchSize; index++)
        {
            var claim = await ClaimDueAsync(timeProvider.GetUtcNow(), cancellationToken);
            if (claim is null) break;
            if (await DeliverAsync(claim, cancellationToken)) delivered++;
        }
        return delivered;
    }

    private async Task<Claim?> ClaimDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH due AS (
                SELECT "Id", "AttemptCount" FROM outbox_messages
                WHERE "Status" = 'Pending' AND "NextAttemptAt" <= @now
                  AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= @now)
                ORDER BY "NextAttemptAt", "Id"
                LIMIT 1 FOR UPDATE SKIP LOCKED)
            UPDATE outbox_messages AS message
            SET "LeaseOwner" = @owner, "LeaseExpiresAt" = @leaseUntil,
                "Generation" = message."Generation" + 1,
                "FirstAttemptAt" = COALESCE(message."FirstAttemptAt", @now),
                "AttemptCount" = LEAST(message."AttemptCount" + 1, 8)
            FROM due WHERE message."Id" = due."Id"
            RETURNING message."Id", message."Generation", due."AttemptCount" >= 8;
            """;
        var owner = Guid.NewGuid().ToString("N");
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("leaseUntil", now.Add(LeaseDuration));
            command.Parameters.AddWithValue("now", now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new Claim(reader.GetGuid(0), owner, reader.GetInt32(1), reader.GetBoolean(2))
                : null;
        }
        finally { if (opened) await connection.CloseAsync(); }
    }

    private IQueryable<OutboxMessage> Owned(Claim claim) =>
        context.OutboxMessages.Where(message => message.Id == claim.Id &&
            message.Status == OutboxMessageStatus.Pending && message.LeaseOwner == claim.Owner && message.Generation == claim.Generation);

    private async Task<bool> DeliverAsync(Claim claim, CancellationToken cancellationToken)
    {
        var message = await Owned(claim).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (message is null) return false;
        if (claim.Exhausted) return await FailAsync(claim, message, "attempts_exhausted", true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (message.FirstAttemptAt is { } firstAttempt && now >= firstAttempt.Add(ReceiptRetention))
            return await FailAsync(claim, message, "receipt_window_expired", true, cancellationToken);

        var handler = handlers.SingleOrDefault(candidate => candidate.MessageType == message.Type);
        if (handler is null) return await FailAsync(claim, message, "handler_missing", false, cancellationToken);
        var secret = await context.OutboxSecrets.AsNoTracking().SingleOrDefaultAsync(item => item.OutboxMessageId == claim.Id, cancellationToken);
        string? token = null;
        if (handler.RequiresSecret)
        {
            if (secret is null) return await FailAsync(claim, message, "secret_missing", false, cancellationToken);
            if (secret.Status != OutboxSecretStatus.Pending || secret.ExpiresAt <= now)
                return await FailAsync(claim, message, "envelope_expired", true, cancellationToken);
            token = await secrets.ReadAsync(claim.Id, cancellationToken);
            if (token is null) return await FailAsync(claim, message, "envelope_unreadable", false, cancellationToken);
        }

        IdentityEmail? email;
        try { email = await handler.PrepareAsync(message.Payload, token, cancellationToken); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { return await FailAsync(claim, message, "payload_invalid", true, cancellationToken); }
        if (email is null) return await FailAsync(claim, message, "recipient_missing", true, cancellationToken);
        var fingerprint = sender.GetRequestFingerprint(email.Recipient, email.Subject, email.Body);
        if (message.RequestFingerprint is { } previous && previous != fingerprint)
            return await FailAsync(claim, message, "request_changed", true, cancellationToken);

        now = timeProvider.GetUtcNow();
        if (message.FirstAttemptAt is { } started && now.AddSeconds(20) >= started.Add(ReceiptRetention))
            return await FailAsync(claim, message, "receipt_window_expired", true, cancellationToken);
        // Renew only the original live claim. Checking the secret here closes expiry while rendering.
        var ready = Owned(claim).Where(item => item.LeaseExpiresAt > now);
        if (handler.RequiresSecret)
            ready = ready.Where(_ => context.OutboxSecrets.Any(item => item.OutboxMessageId == claim.Id && item.Status == OutboxSecretStatus.Pending && item.ExpiresAt > now));
        var renewed = await ready.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.RequestFingerprint, fingerprint)
            .SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration)), cancellationToken);
        if (renewed == 0) return false;

        EmailDeliveryReceipt receipt;
        try { receipt = await sender.SendAsync(email.Recipient, email.Subject, email.Body, claim.Id.ToString(), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return await FailAsync(claim, message, "provider_error", false, cancellationToken); }
        if (!receipt.Delivered)
            return await FailAsync(claim, message, receipt.IsPermanentFailure ? "provider_rejected" : "provider_unavailable", receipt.IsPermanentFailure, cancellationToken);
        if (string.IsNullOrWhiteSpace(receipt.ProviderReceipt))
            return await FailAsync(claim, message, "provider_receipt_missing", false, cancellationToken);
        return await SettleAsync(claim, OutboxMessageStatus.Delivered, null, null, receipt.ProviderReceipt, cancellationToken);
    }

    private Task<bool> FailAsync(Claim claim, OutboxMessage message, string code, bool permanent, CancellationToken cancellationToken)
    {
        var abandoned = permanent || message.AttemptCount >= MaxAttempts;
        var retry = timeProvider.GetUtcNow().AddSeconds(Math.Min(30 * Math.Pow(2, message.AttemptCount - 1), 1800));
        return SettleAsync(claim, abandoned ? OutboxMessageStatus.Abandoned : OutboxMessageStatus.Pending, code, retry, null, cancellationToken);
    }

    private Task<bool> SettleAsync(Claim claim, OutboxMessageStatus status, string? code, DateTimeOffset? nextAttempt, string? receipt, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(() => SettleLocalAsync(claim, status, code, nextAttempt, receipt, cancellationToken));

    private async Task<bool> SettleLocalAsync(Claim claim, OutboxMessageStatus status, string? code, DateTimeOffset? nextAttempt, string? receipt, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var changed = await Owned(claim).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, status)
            .SetProperty(item => item.FailureCode, code)
            .SetProperty(item => item.DeliveredAt, status == OutboxMessageStatus.Delivered ? now : (DateTimeOffset?)null)
            .SetProperty(item => item.NextAttemptAt, item => nextAttempt ?? item.NextAttemptAt)
            .SetProperty(item => item.LeaseOwner, (string?)null)
            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
        if (changed == 0) return false; // A stale worker changes neither the new claim nor its envelope.
        var secret = await context.OutboxSecrets.FromSqlInterpolated(
            $"""SELECT * FROM outbox_secrets WHERE "OutboxMessageId" = {claim.Id} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (secret is not null)
        {
            await context.Entry(secret).ReloadAsync(cancellationToken);
            if (secret.Status == OutboxSecretStatus.Pending)
            {
                if (status == OutboxMessageStatus.Delivered) secret.MarkDelivered("delivered", now, receipt!);
                if (status == OutboxMessageStatus.Abandoned)
                    secret.Terminate(code == "envelope_expired" ? OutboxSecretStatus.Expired : OutboxSecretStatus.Failed, code!, now);
            }
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return status == OutboxMessageStatus.Delivered;
        }
        finally { if (secret is not null) context.Entry(secret).State = EntityState.Detached; }
    }

    private sealed record Claim(Guid Id, string Owner, int Generation, bool Exhausted);
}

public interface IOutboxDeliveryHandler
{
    string MessageType { get; }
    bool RequiresSecret => true;
    Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken);
}
