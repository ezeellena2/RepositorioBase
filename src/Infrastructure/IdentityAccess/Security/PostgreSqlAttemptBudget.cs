using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Security;

/// <summary>
/// The one adapter behind <see cref="ISharedAttemptBudget"/>: attempt state in the database the deployment already
/// has, so a limit holds across instances and restarts without a second infrastructure product (IA-REQ-057).
/// <para>
/// It opens its own connection rather than using the ambient one on purpose. A budget spent inside a business
/// transaction that later rolls back would be refunded by the rollback, which is exactly what an attacker whose
/// attempt failed would want.
/// </para>
/// </summary>
public sealed class PostgreSqlAttemptBudget(ApplicationDbContext context, TimeProvider timeProvider) : ISharedAttemptBudget
{
    /// <summary>What a caller is told to wait when the store itself is unreachable. A **product default**.</summary>
    private static readonly TimeSpan OutageRetryAfter = TimeSpan.FromSeconds(30);

    private const string Spend = """
        INSERT INTO "IdentityAttemptBudgets" ("Scope", "KeyHash", "WindowStart", "Count", "ExpiresAt")
        VALUES (@scope, @keyHash, @windowStart, 1, @expiresAt)
        ON CONFLICT ("Scope", "KeyHash", "WindowStart")
        DO UPDATE SET "Count" = "IdentityAttemptBudgets"."Count" + 1
        WHERE "IdentityAttemptBudgets"."Count" < @limit
        RETURNING "Count";
        """;

    public async Task<AttemptBudgetDecision> SpendAsync(AttemptBudget budget, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (budget.Limit < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        if (budget.Window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));

        var now = timeProvider.GetUtcNow();
        var windowStart = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % budget.Window.Ticks), TimeSpan.Zero);
        var expiresAt = windowStart + budget.Window;

        try
        {
            var connectionString = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The attempt budget store has no connection string.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(Spend, connection);
            command.Parameters.AddWithValue("scope", budget.Scope);
            command.Parameters.AddWithValue("keyHash", Digest(budget.Scope, key));
            command.Parameters.AddWithValue("windowStart", windowStart);
            command.Parameters.AddWithValue("expiresAt", expiresAt);
            command.Parameters.AddWithValue("limit", budget.Limit);

            var admitted = await command.ExecuteScalarAsync(cancellationToken);

            // No row comes back when the conditional update matched nothing, which is what "already at the limit"
            // looks like from here. PostgreSQL decided it under the row lock, so parallel callers cannot both win.
            return admitted is null
                ? new AttemptBudgetDecision(AttemptBudgetOutcome.Exhausted, expiresAt - now)
                : new AttemptBudgetDecision(AttemptBudgetOutcome.Admitted, TimeSpan.Zero);
        }
        catch (Exception failure) when (failure is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            // Fail closed, and say what actually happened. Answering "too many attempts" here would tell a person
            // they did something they did not do, and would hide an outage from whoever is watching (amendment A5).
            return new AttemptBudgetDecision(AttemptBudgetOutcome.Unavailable, OutageRetryAfter);
        }
    }

    /// <summary>
    /// The stored key. Scoping the digest keeps one budget's keys from colliding with another's, and hashing it keeps
    /// the table from becoming a list of who attempted what (IA-REQ-029).
    /// </summary>
    private static string Digest(string scope, string key) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{key.Trim().ToLowerInvariant()}")));
}
