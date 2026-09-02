using Aspire.Hosting;
using Npgsql;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Playwright;

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

        _credentials = new Credentials($"Acceptance!{Guid.NewGuid():N}a1", string.Empty);
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

        var sessionCredentials = credentials.CreateSessionCredentials();
        await ProvisionConfirmedActiveUserAsync(AspireSetup.App, sessionCredentials, CancellationToken.None);
        await GrantApplicationPermissionsAsync(AspireSetup.App, sessionCredentials, CancellationToken.None);
        await loginPage.SetAuthenticationCookieAsync(await CreateSessionAsync(AspireSetup.App, sessionCredentials, CancellationToken.None));
    }

    public static async Task AssertAuthenticatedAsync(LoginPage loginPage)
    {
        var credentials = _credentials ?? throw new InvalidOperationException(
            "Acceptance account provisioning must complete before authenticated scenarios run.");

        (await loginPage.HasAuthenticationCookieAsync()).ShouldBeTrue();

        using var client = CreateCookieIsolatedClient(AspireSetup.App);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/context");
        request.Headers.TryAddWithoutValidation("Cookie", loginPage.GetAuthenticationCookie());
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    public static async Task SignInWithInvalidCredentialsAsync(LoginPage loginPage)
    {
        using var client = CreateCookieIsolatedClient(AspireSetup.App);
        var origin = GetOrigin(client);
        var antiforgery = await GetAntiforgeryAsync(client, origin, cancellationToken: default);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/sessions")
        {
            Content = JsonContent.Create(new { email = "hacker@localhost", password = "l337hax!" })
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", antiforgery.RequestToken);
        request.Headers.TryAddWithoutValidation("Cookie", antiforgery.Cookie);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
        (await loginPage.HasAuthenticationCookieAsync()).ShouldBeFalse();
    }

    private static async Task ProvisionConfirmedActiveUserAsync(DistributedApplication app, Credentials credentials, CancellationToken cancellationToken)
    {
        var connectionString = await app.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Acceptance database connection string is unavailable.");
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            UserName = credentials.Email,
            Email = credentials.Email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        user.NormalizedUserName = credentials.Email.ToUpperInvariant();
        user.NormalizedEmail = credentials.Email.ToUpperInvariant();
        user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, credentials.Password);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await WaitForIdentitySchemaAsync(connection, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnd", "LockoutEnabled", "AccessFailedCount")
            VALUES (@id, @userName, @normalizedUserName, @email, @normalizedEmail, TRUE, @passwordHash, @securityStamp, @concurrencyStamp, NULL, FALSE, FALSE, NULL, TRUE, 0);
            """, connection);
        command.Parameters.AddWithValue("id", user.Id);
        command.Parameters.AddWithValue("userName", user.UserName!);
        command.Parameters.AddWithValue("normalizedUserName", user.NormalizedUserName!);
        command.Parameters.AddWithValue("email", user.Email!);
        command.Parameters.AddWithValue("normalizedEmail", user.NormalizedEmail!);
        command.Parameters.AddWithValue("passwordHash", user.PasswordHash!);
        command.Parameters.AddWithValue("securityStamp", user.SecurityStamp!);
        command.Parameters.AddWithValue("concurrencyStamp", user.ConcurrencyStamp!);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WaitForIdentitySchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT to_regclass('\"AspNetUsers\"') IS NOT NULL;", connection);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private static async Task<string> CreateSessionAsync(DistributedApplication app, Credentials credentials, CancellationToken cancellationToken)
    {
        using var client = CreateCookieIsolatedClient(app);
        var origin = GetOrigin(client);
        var antiforgery = await GetAntiforgeryAsync(client, origin, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/sessions")
        {
            Content = JsonContent.Create(new { email = credentials.Email, password = credentials.Password })
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", antiforgery.RequestToken);
        request.Headers.TryAddWithoutValidation("Cookie", antiforgery.Cookie);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        return GetCookie(response, "__Host-ia-auth");
    }

    private static async Task<AntiforgeryPair> GetAntiforgeryAsync(HttpClient client, string origin, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/identity/antiforgery", cancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryResponsePayload>(cancellationToken);
        payload.ShouldNotBeNull();
        return new AntiforgeryPair(GetCookie(response, "__Host-XSRF-TOKEN"), payload!.RequestToken);
    }

    private static string GetOrigin(HttpClient client) =>
        client.BaseAddress?.GetLeftPart(UriPartial.Authority)
        ?? throw new InvalidOperationException("Acceptance Web API address is unavailable.");

    private static HttpClient CreateCookieIsolatedClient(DistributedApplication app) => new(new HttpClientHandler
    {
        UseCookies = false
    })
    {
        BaseAddress = new Uri(app.GetEndpoint(Services.WebApi).ToString())
    };

    private static string GetCookie(HttpResponseMessage response, string cookieName)
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];
        var setCookie = cookies.SingleOrDefault(value => value.StartsWith($"{cookieName}=", StringComparison.Ordinal));
        if (setCookie is null)
        {
            var issuedNames = string.Join(", ", cookies.Select(value => value.Split('=', 2)[0]));
            throw new InvalidOperationException($"Expected cookie '{cookieName}' was not issued by HTTP {(int)response.StatusCode}. Issued cookie names: {issuedNames}.");
        }

        return setCookie.Split(';', 2)[0];
    }

    private sealed record AntiforgeryPair(string Cookie, string RequestToken);

    private sealed record AntiforgeryResponsePayload(string RequestToken);

    private sealed record Credentials(string Password, string Email)
    {
        public Credentials CreateSessionCredentials() => new(
            Password,
            $"acceptance-{Guid.NewGuid():N}@example.test");
    }
}
