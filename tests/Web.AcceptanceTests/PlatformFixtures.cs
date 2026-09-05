using CleanArchitecture.Domain.IdentityAccess.Security;
using Npgsql;

namespace CleanArchitecture.Web.AcceptanceTests;

/// <summary>
/// The state a Platform journey starts from, read or seeded directly in PostgreSQL.
/// <para>
/// The bootstrap ceremony itself is not seeded — the application performs it as it starts, and the first scenario
/// asserts that it did. What is seeded is an administrator invitation whose token the test chose, because the
/// token the application mints leaves the process only inside an encrypted envelope that this process has no key
/// for. Everything a scenario actually asserts still goes through the browser.
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

    /// <summary>Marks the message carrying the owner's token as permanently undeliverable, which is what recovery is for.</summary>
    internal static async Task FailOwnerDeliveryAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE outbox_messages SET \"Status\" = 'Abandoned', \"FailureCode\" = 'provider_rejected', \"LeaseOwner\" = NULL, \"LeaseExpiresAt\" = NULL " +
            "WHERE \"Id\" IN (SELECT \"DeliveryMessageId\" FROM \"PlatformAdminInvitations\" WHERE \"IsOwner\" AND \"Status\" = 'Pending');",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A pending administrator invitation whose token the test chose.</summary>
    internal static async Task<(string Email, string Token)> AdministratorInvitationAsync()
    {
        var tenantId = await PlatformTenantIdAsync();
        var email = $"platform-admin-{Guid.NewGuid():N}@example.test";
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"PlatformAdminInvitations\" (\"Id\", \"TenantId\", \"NormalizedEmail\", \"TokenHash\", \"Status\", \"Delivery\", \"IsOwner\", \"CreatedAt\", \"ExpiresAt\") " +
            "VALUES (gen_random_uuid(), @tenantId, @email, @hash, 'Pending', 'Pending', FALSE, NOW() - INTERVAL '1 minute', NOW() + INTERVAL '7 days');",
            connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", VersionedTokenHash.Of(token).Value);
        await command.ExecuteNonQueryAsync();
        return (email, token);
    }

    /// <summary>
    /// Confirms the address the way the recipient would. The confirmation token is sealed in an envelope this
    /// process holds no key for, so the transition the endpoint performs is applied directly — what the journey
    /// proves is what happens after confirmation, not the cryptography of the link.
    /// </summary>
    internal static async Task ConfirmAsync(string email)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE \"AspNetUsers\" SET \"EmailConfirmed\" = TRUE WHERE \"NormalizedEmail\" = @email;", connection);
        command.Parameters.AddWithValue("email", email.ToUpperInvariant());
        await command.ExecuteNonQueryAsync();
    }

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
