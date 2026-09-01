using Aspire.Hosting;
using Npgsql;
using System.Net.Http.Json;

namespace CleanArchitecture.Web.AcceptanceTests;

internal static class AcceptanceTestCredentials
{
    private const string ApplicationPermissionClaimType = "permission";
    private static Credentials? _credentials;

    public static async Task CreateAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        if (_credentials is not null)
        {
            return;
        }

        var credentials = new Credentials(
            $"acceptance-{Guid.NewGuid():N}@example.test",
            $"Acceptance!{Guid.NewGuid():N}a1");

        using var client = app.CreateHttpClient(Services.WebApi);
        using var response = await client.PostAsJsonAsync(
            "/api/Users/register",
            new { email = credentials.Email, password = credentials.Password },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acceptance account provisioning failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        await GrantApplicationPermissionsAsync(app, credentials, cancellationToken);

        _credentials = credentials;
    }

    private static async Task GrantApplicationPermissionsAsync(DistributedApplication app, Credentials credentials, CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Acceptance database connection string is unavailable.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var permission in new[] { "todos.read", "todos.write", "weather.read" })
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO "AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
                SELECT "Id", @claimType, @claimValue
                FROM "AspNetUsers"
                WHERE "NormalizedEmail" = @email;
                """, connection);
            command.Parameters.AddWithValue("claimType", ApplicationPermissionClaimType);
            command.Parameters.AddWithValue("claimValue", permission);
            command.Parameters.AddWithValue("email", credentials.Email.ToUpperInvariant());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static async Task SignInAsync(LoginPage loginPage)
    {
        var credentials = _credentials ?? throw new InvalidOperationException(
            "Acceptance account provisioning must complete before authenticated scenarios run.");

        await loginPage.SetEmail(credentials.Email);
        await loginPage.SetPassword(credentials.Password);
        await loginPage.ClickLogin();
    }

    private sealed record Credentials(string Email, string Password);
}
