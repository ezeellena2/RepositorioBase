using Aspire.Hosting;
using System.Net.Http.Json;

namespace CleanArchitecture.Web.AcceptanceTests;

internal static class AcceptanceTestCredentials
{
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

        _credentials = credentials;
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
