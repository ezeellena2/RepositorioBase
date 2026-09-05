using Npgsql;

namespace CleanArchitecture.Web.AcceptanceTests;

/// <summary>
/// The state a Platform journey starts from, read or seeded directly in PostgreSQL.
/// <para>
/// No part of the ceremony is seeded — the application performs it as it starts, and the walk asserts that it
/// did. What is written here is what stands outside the ceremony: an organization for the panel to operate on,
/// and the one premise recovery exists for, a delivery that failed. Everything a scenario asserts about identity,
/// confirmation or authority goes through the browser.
/// </para>
/// </summary>
internal static class PlatformFixtures
{
    internal const string Password = IdentityAccessFixtures.Password;

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connectionString = await AspireSetup.App.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Acceptance database connection string is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Waits for the ceremony the host runs at start-up to have produced its rows. The schema is waited for
    /// first and separately: the application migrates on startup, so a fixture can reach the database before the
    /// table exists, and querying it then is an error rather than an empty answer.
    /// </summary>
    internal static async Task<Guid> PlatformTenantIdAsync()
    {
        await using var connection = await OpenAsync();
        for (var attempt = 0; attempt < 300; attempt++)
        {
            await using var schema = new NpgsqlCommand(
                "SELECT to_regclass('\"PlatformAdminInvitations\"') IS NOT NULL;", connection);
            if ((bool)(await schema.ExecuteScalarAsync())!)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT \"Id\" FROM \"Tenants\" WHERE \"Type\" = 'Platform' LIMIT 1;", connection);
                if (await command.ExecuteScalarAsync() is Guid id) return id;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new InvalidOperationException("The Platform tenant was never created.");
    }

    internal static async Task<long> CountAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    internal static Task<long> PendingOwnerInvitationsAsync() =>
        CountAsync("SELECT count(*) FROM \"PlatformAdminInvitations\" WHERE \"IsOwner\" AND \"Status\" = 'Pending';");

    internal static Task<long> PlatformMembershipsAsync() =>
        CountAsync("SELECT count(*) FROM \"TenantMemberships\" m JOIN \"Tenants\" t ON t.\"Id\" = m.\"TenantId\" WHERE t.\"Type\" = 'Platform';");

    /// <summary>
    /// Marks the message carrying the owner's token as permanently undeliverable, which is the state recovery
    /// exists for. The delivery timestamp is cleared with it: a message cannot be both abandoned and delivered,
    /// and the row constraint says so, which is exactly why the premise has to be set honestly.
    /// </summary>
    internal static async Task FailOwnerDeliveryAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE outbox_messages SET \"Status\" = 'Abandoned', \"FailureCode\" = 'provider_rejected', " +
            "\"DeliveredAt\" = NULL, \"LeaseOwner\" = NULL, \"LeaseExpiresAt\" = NULL " +
            "WHERE \"Id\" IN (SELECT \"DeliveryMessageId\" FROM \"PlatformAdminInvitations\" WHERE \"IsOwner\" AND \"Status\" = 'Pending');",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// One delivered message, read out of the run's drop folder the way its recipient would read their mail.
    /// <para>
    /// This is the only way a test can follow a real link: the tokens are sealed with Data Protection keys held
    /// by the application process, so nothing outside it can read one out of the database. Waiting for the file
    /// is waiting for the dispatcher to have delivered it, which is the same thing a person waits for.
    /// </para>
    /// </summary>
    /// <param name="excluding">
    /// Messages already read, by drop-file name. Recovery rotates the token, so after it the folder holds two
    /// invitations for the same address and only the newer one still opens anything; naming the older one is how
    /// a caller says which it already has, without guessing from a clock.
    /// </param>
    internal static async Task<DeliveredMessage> DeliveredAsync(
        string recipient, string subjectContains, IReadOnlyCollection<string>? excluding = null)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (Directory.Exists(AspireSetup.MailDropPath))
            {
                foreach (var file in Directory.EnumerateFiles(AspireSetup.MailDropPath, "*.txt"))
                {
                    var name = Path.GetFileName(file);
                    if (excluding?.Contains(name, StringComparer.OrdinalIgnoreCase) == true) continue;

                    string content;
                    try { content = await File.ReadAllTextAsync(file); }
                    catch (IOException) { continue; } // Still being written.

                    if (!content.Contains($"To: {recipient}", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!content.Contains(subjectContains, StringComparison.OrdinalIgnoreCase)) continue;

                    var link = System.Text.RegularExpressions.Regex.Match(content, @"https?://\S+");
                    if (!link.Success) continue;
                    var uri = new Uri(link.Value.TrimEnd('.'));
                    return new DeliveredMessage(recipient, content, uri.AbsolutePath, uri.Fragment, name);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // What the dispatcher decided is the only useful thing to say here: "no file appeared" is the symptom,
        // and the outbox row records the cause — an unreadable envelope, a missing handler, a refused provider.
        var outbox = await ScalarAsync(
            "SELECT string_agg(\"Type\" || '=' || \"Status\" || '/' || COALESCE(\"FailureCode\", '-') || 'x' || \"AttemptCount\" || ' due=' || (\"NextAttemptAt\" <= NOW())::text || ' lease=' || COALESCE(\"LeaseOwner\", '-'), ', ') FROM outbox_messages;");
        // Whether the drop folder exists says which half failed: the sender creates it as it validates its
        // configuration, so an absent folder means the dispatcher never got as far as a sender at all.
        var drop = Directory.Exists(AspireSetup.MailDropPath)
            ? $"{Directory.GetFiles(AspireSetup.MailDropPath).Length} file(s)"
            : "absent";
        throw new InvalidOperationException(
            $"No message reached {recipient} about \"{subjectContains}\". Outbox: {outbox ?? "empty"}. " +
            $"Drop: {drop}. Worker: {await AspireSetup.WorkerLogTailAsync()}");
    }

    /// <summary>
    /// A message as it was delivered, split into the parts a recipient acts on. The path and fragment are kept
    /// separately because the run's frontend lives on a port only the host knows, so the journey opens the same
    /// page and the same token on the origin it actually has. <c>DropFile</c> names the message
    /// itself, which is how a later read says which mail it has already seen.
    /// </summary>
    internal sealed record DeliveredMessage(string Recipient, string Body, string Path, string Fragment, string DropFile);

    internal static async Task<Guid> ActiveOrganizationAsync(string slug)
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"Tenants\" (\"Id\", \"Type\", \"Status\", \"Slug\", \"AuthorizationVersion\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES (@id, 'Organization', 'Active', @slug, 0, NOW(), NOW());", connection);
        command.Parameters.AddWithValue("id", tenantId);
        command.Parameters.AddWithValue("slug", slug);
        await command.ExecuteNonQueryAsync();
        return tenantId;
    }

    internal static Task<string?> OrganizationStatusAsync(string slug) =>
        ScalarAsync($"SELECT \"Status\" || ':' || COALESCE(\"SuspensionReason\", '-') FROM \"Tenants\" WHERE \"Slug\" = '{slug}';");

    /// <summary>What a real authenticator would show for this key now, computed here rather than asked of the code.</summary>
    internal static string TotpCode(string sharedKey)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var character in sharedKey.Trim().TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(char.ToUpperInvariant(character));
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
            bitsLeft -= 8;
        }

        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        Span<byte> mac = stackalloc byte[20];
        System.Security.Cryptography.HMACSHA1.HashData([.. bytes], counter, mac);
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
