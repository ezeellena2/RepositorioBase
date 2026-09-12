using CleanArchitecture.Domain.IdentityAccess.Security;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace CleanArchitecture.Web.AcceptanceTests;

/// <summary>
/// Seeds the state a journey starts from, directly in PostgreSQL.
/// <para>
/// These rows are the premise of a scenario, not the thing under test: a journey about switching organizations
/// should fail when switching breaks, not when something upstream of it does. Everything a scenario actually
/// asserts still goes through the browser.
/// </para>
/// </summary>
internal static class IdentityAccessFixtures
{
    internal const string Password = "Acceptance1234!";
    private static readonly int[] CuitWeights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    internal sealed record SeededIdentity(Guid Id, string Email);

    /// <summary>
    /// Reads back the identifier of an identity the journey created. It replaces the one the SQL confirmation
    /// used to return as a side effect of activating the account: reading does not bypass a journey, the writes
    /// did.
    /// </summary>
    internal static async Task<Guid> IdentityIdAsync(string email)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = @email;", connection);
        command.Parameters.AddWithValue("email", email.ToUpperInvariant());
        return await command.ExecuteScalarAsync() is Guid id
            ? id
            : throw new InvalidOperationException($"No identity was registered for {email}.");
    }

    /// <summary>
    /// The slug is what a tenant is known by (SPEC section on the Tenant aggregate), so it is what the identity
    /// context carries and what the browser shows. The legal name lives on the profile and is deliberately not
    /// part of that projection, so a scenario naming an organization has to name it by its slug.
    /// </summary>
    internal sealed record SeededOrganization(Guid TenantId, string Slug, Guid RoleId);

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connectionString = await AspireSetup.App.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Acceptance database connection string is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await WaitForSchemaAsync(connection);
        return connection;
    }

    /// <summary>
    /// The application migrates on startup, so a fixture can reach the database before the tables exist.
    /// Waiting here rather than failing turns a race into the ordering it actually is.
    /// </summary>
    private static async Task WaitForSchemaAsync(NpgsqlConnection connection)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            // The tables *and* the columns a later migration adds. `AspNetUsers` appears with the baseline and
            // grows afterwards, so a fixture that only waited for the table would write into a half-migrated one.
            await using var command = new NpgsqlCommand(
                """
                SELECT to_regclass('"AspNetUsers"') IS NOT NULL
                   AND to_regclass('"Invitations"') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'AspNetUsers' AND column_name = 'Status');
                """, connection);
            if ((bool)(await command.ExecuteScalarAsync())!) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new InvalidOperationException("The identity schema did not appear before the fixtures needed it.");
    }

    /// <summary>
    /// The permission catalogue is written by the application as it starts, so a fixture can reach a migrated
    /// database whose catalogue is still empty and have its role fail the foreign key. Waiting for the exact
    /// codes a scenario grants turns that race into the ordering it is.
    /// </summary>
    private static async Task WaitForPermissionsAsync(NpgsqlConnection connection, string[] codes)
    {
        if (codes.Length == 0) return;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM \"Permissions\" WHERE \"Code\" = ANY(@codes);", connection);
            command.Parameters.AddWithValue("codes", codes);
            if ((long)(await command.ExecuteScalarAsync())! == codes.Distinct().Count()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new InvalidOperationException($"The permissions {string.Join(", ", codes)} were never seeded.");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<SeededIdentity> ConfirmedIdentityAsync()
    {
        var identityId = Guid.NewGuid();
        var email = $"acceptance-{identityId:N}@example.test";
        var user = new IdentityUser<Guid> { Id = identityId, UserName = email, Email = email };
        var hash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, Password);

        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            // `Status` is stated rather than left to its default. The persisted status is what decides whether an
            // identity may sign in, and its default is the fail-closed `PendingConfirmation` — a premise that said
            // "confirmed" only in `EmailConfirmed` would be seeding an account that cannot sign in.
            "INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\", \"Status\") " +
            "VALUES (@id, @email, @normalized, @email, @normalized, TRUE, @hash, @stamp, @stamp, FALSE, FALSE, TRUE, 0, 'Active');",
            ("id", identityId), ("email", email), ("normalized", email.ToUpperInvariant()), ("hash", hash), ("stamp", Guid.NewGuid().ToString("N")));
        return new SeededIdentity(identityId, email);
    }

    /// <summary>An active organization with one role carrying the permissions named.</summary>
    internal static async Task<SeededOrganization> OrganizationAsync(string name, params string[] permissionCodes)
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var slug = $"{name.ToLowerInvariant()}-{tenantId:N}";
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO \"Tenants\" (\"Id\", \"Type\", \"Status\", \"Slug\", \"AuthorizationVersion\") VALUES (@id, 'Organization', 'Active', @slug, 0);",
            ("id", tenantId), ("slug", slug));
        await ExecuteAsync(
            connection,
            "INSERT INTO \"OrganizationProfiles\" (\"TenantId\", \"LegalName\", \"Cuit\") VALUES (@tenantId, @name, @cuit);",
            ("tenantId", tenantId), ("name", name), ("cuit", NextCuit()));
        await ExecuteAsync(
            connection,
            "INSERT INTO \"Roles\" (\"Id\", \"TenantId\", \"Name\", \"NormalizedName\", \"IsSystem\", \"IsRetired\") VALUES (@id, @tenantId, @name, @normalized, FALSE, FALSE);",
            ("id", roleId), ("tenantId", tenantId), ("name", $"role-{roleId:N}"), ("normalized", $"ROLE-{roleId:N}".ToUpperInvariant()));
        await WaitForPermissionsAsync(connection, permissionCodes);
        foreach (var code in permissionCodes)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO \"RolePermissions\" (\"TenantId\", \"RoleId\", \"PermissionCode\") VALUES (@tenantId, @roleId, @code);",
                ("tenantId", tenantId), ("roleId", roleId), ("code", code));
        }

        return new SeededOrganization(tenantId, slug, roleId);
    }

    internal static async Task MembershipAsync(SeededOrganization organization, SeededIdentity identity)
    {
        var membershipId = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO \"TenantMemberships\" (\"Id\", \"TenantId\", \"IdentityId\", \"Status\") VALUES (@id, @tenantId, @identityId, 'Active');",
            ("id", membershipId), ("tenantId", organization.TenantId), ("identityId", identity.Id));
        await ExecuteAsync(
            connection,
            "INSERT INTO \"MembershipRoles\" (\"TenantId\", \"MembershipId\", \"RoleId\") VALUES (@tenantId, @membershipId, @roleId);",
            ("tenantId", organization.TenantId), ("membershipId", membershipId), ("roleId", organization.RoleId));
    }

    /// <summary>
    /// A pending invitation whose token the test chose. The row stores only the hash, and it is produced by the
    /// same type the application uses rather than by a copy of its rule that could drift from it.
    /// </summary>
    internal static async Task<string> InvitationAsync(SeededOrganization organization, string recipient)
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var invitationId = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO \"Invitations\" (\"Id\", \"TenantId\", \"TokenHash\", \"NormalizedEmail\", \"Status\", \"CreatedAt\", \"ExpiresAt\") " +
            "VALUES (@id, @tenantId, @hash, @email, 'Pending', NOW() - INTERVAL '1 minute', NOW() + INTERVAL '7 days');",
            ("id", invitationId), ("tenantId", organization.TenantId), ("hash", VersionedTokenHash.Of(token).Value), ("email", recipient.ToLowerInvariant()));
        await ExecuteAsync(
            connection,
            "INSERT INTO \"InvitationRoles\" (\"TenantId\", \"InvitationId\", \"RoleId\") VALUES (@tenantId, @invitationId, @roleId);",
            ("tenantId", organization.TenantId), ("invitationId", invitationId), ("roleId", organization.RoleId));
        return token;
    }

    /// <summary>
    /// An organization whose administrator may run it: read and manage members, read and manage roles, and
    /// invite — the last because the roles screen offers only permissions the actor holds, so an administrator
    /// who could not invite could not put inviting into a role either. Their membership is the owner.
    /// </summary>
    internal static async Task<SeededOrganization> AdministeredOrganizationAsync(SeededIdentity administrator)
    {
        var organization = await OrganizationAsync(
            "Acme", "members.read", "members.manage", "roles.read", "roles.manage", "members.invite",
            "tenant.ownership.transfer");
        await MembershipAsync(organization, administrator);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "UPDATE \"Tenants\" SET \"OwnerMembershipId\" = (SELECT \"Id\" FROM \"TenantMemberships\" WHERE \"TenantId\" = @tenantId AND \"IdentityId\" = @identityId) WHERE \"Id\" = @tenantId;",
            ("tenantId", organization.TenantId), ("identityId", administrator.Id));
        return organization;
    }

    /// <summary>A membership with no role at all, which is what "holds nothing" has to mean.</summary>
    internal static async Task PlainMembershipAsync(SeededOrganization organization, SeededIdentity identity)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO \"TenantMemberships\" (\"Id\", \"TenantId\", \"IdentityId\", \"Status\") VALUES (@id, @tenantId, @identityId, 'Active');",
            ("id", Guid.NewGuid()), ("tenantId", organization.TenantId), ("identityId", identity.Id));
    }

    /// <summary>Which identity the organization currently belongs to, read back through its owning membership.</summary>
    internal static async Task<Guid?> OwnerIdentityIdAsync(SeededOrganization organization)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT m."IdentityId"
            FROM "Tenants" t JOIN "TenantMemberships" m ON m."Id" = t."OwnerMembershipId"
            WHERE t."Id" = @tenantId;
            """, connection);
        command.Parameters.AddWithValue("tenantId", organization.TenantId);
        return await command.ExecuteScalarAsync() as Guid?;
    }

    internal static async Task<long> MembershipCountAsync(SeededOrganization organization, Guid identityId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM \"TenantMemberships\" WHERE \"TenantId\" = @tenantId AND \"IdentityId\" = @identityId;", connection);
        command.Parameters.AddWithValue("tenantId", organization.TenantId);
        command.Parameters.AddWithValue("identityId", identityId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task RevokeSessionsAsync(Guid identityId)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "UPDATE \"UserSessions\" SET \"RevokedAt\" = NOW() WHERE \"IdentityId\" = @identityId AND \"RevokedAt\" IS NULL;",
            ("identityId", identityId));
    }

    internal static async Task LockOutAsync(Guid identityId)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "UPDATE \"AspNetUsers\" SET \"LockoutEnd\" = NOW() + INTERVAL '1 hour' WHERE \"Id\" = @identityId;",
            ("identityId", identityId));
    }

    /// <summary>
    /// Moves the lockout into the past, which is the only thing waiting out a lockout does. Waiting for real
    /// would make the test slower than the window it is proving, and sleeping for less would prove nothing.
    /// </summary>
    internal static async Task ExpireLockOutAsync(Guid identityId)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "UPDATE \"AspNetUsers\" SET \"LockoutEnd\" = NOW() - INTERVAL '1 minute', \"AccessFailedCount\" = 0 WHERE \"Id\" = @identityId;",
            ("identityId", identityId));
    }

    /// <summary>
    /// Eleven digits with no separators, which is the normalized form the column stores and the only form
    /// that fits it. Drawn at random rather than counted, because the acceptance database outlives a run and
    /// a counter would collide with the previous one on the unique index.
    /// </summary>
    internal static string NextCuit()
    {
        while (true)
        {
            var serial = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
            var digits = $"30{serial:D8}";
            var sum = CuitWeights.Select((weight, index) => weight * (digits[index] - '0')).Sum();
            var checkDigit = (11 - sum % 11) % 11;
            if (checkDigit != 10) return $"{digits}{checkDigit}";
        }
    }
}
