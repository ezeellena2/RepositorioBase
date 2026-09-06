using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Credentials;

/// <summary>
/// What a person can be told about their own credential, and nothing else (SPEC §14.4's credentials row).
/// <para>
/// It exists because two screens are dishonest without it. An account whose only way in is a provider is shown an
/// unlink button that can only ever be refused, and a change-password form for a password that does not exist.
/// Both are questions only the server can answer, so it answers them — and answers nothing more: no hash, no
/// address, no provider identifier, and nothing at all about anybody else.
/// </para>
/// </summary>
public sealed class OwnCredentialsTests : TestBase
{
    private const string Password = "Testing1234!";

    private static string Host() => $"https://credentials-{Guid.NewGuid():N}.localhost";

    [Test]
    public async Task An_identity_with_a_password_is_told_so_and_when_it_last_changed()
    {
        var email = $"holder-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email);

        var before = await ReadAsync(client, host, cookie);

        before.HasPassword.ShouldBeTrue();
        before.PasswordUpdatedAt.ShouldBeNull("nothing has changed it, and a creation date is not a change date");

        await ChangePasswordAsync(client, host, cookie);
        var after = await ReadAsync(client, host, await SignInAsync(client, host, email, "Replaced5678!"));

        after.HasPassword.ShouldBeTrue();
        after.PasswordUpdatedAt.ShouldNotBeNull("a change is what stamps it");
    }

    [Test]
    public async Task Linking_a_provider_does_not_look_like_a_password_change()
    {
        var email = $"linker-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email);

        // Advancing the security version is what every authenticator change does. Only a password change may
        // stamp the password's own timestamp, or the screen would say the password changed when it did not.
        await AdvanceSecurityVersionAsync(identityId);

        (await ReadAsync(client, host, cookie)).PasswordUpdatedAt
            .ShouldBeNull("the version moved, the password did not");
    }

    [Test]
    public async Task The_answer_carries_nothing_but_those_two_members_and_needs_a_session()
    {
        var email = $"quiet-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email);

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/credentials");
        (await client.SendAsync(anonymous)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/credentials");
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        var user = await IdentityHttpHarness.GetUserAsync((await TestApp.ListAsync<ApplicationUser>()).Single().Id);
        body.Contains(email, StringComparison.OrdinalIgnoreCase).ShouldBeFalse("the address is not credential state");
        body.Contains(user.PasswordHash!, StringComparison.Ordinal).ShouldBeFalse();
        body.Contains(Password, StringComparison.Ordinal).ShouldBeFalse();
        System.Text.Json.JsonDocument.Parse(body).RootElement.EnumerateObject().Select(member => member.Name)
            .ShouldBe(["hasPassword", "passwordUpdatedAt"], ignoreOrder: true);
    }

    private static async Task<CredentialsRow> ReadAsync(HttpClient client, string host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/credentials");
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CredentialsRow>())!;
    }

    private static async Task ChangePasswordAsync(HttpClient client, string host, string cookie)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var prove = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/reauthenticate", new { action = ProofActions.PasswordChange, password = Password }, antiforgery.Token);
        prove.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        (await client.SendAsync(prove)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var next = await AntiforgeryAsync(client, host, cookie);
        using var change = IdentityHttpHarness.JsonRequest(HttpMethod.Put, $"{host}/api/identity/credentials/password", new { newPassword = "Replaced5678!" }, next.Token);
        change.Headers.Add("Cookie", $"{cookie}; {next.Cookie}");
        (await client.SendAsync(change)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AdvanceSecurityVersionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRecentIdentityProofStore>().AdvanceVersionAsync(identityId, CancellationToken.None);
    }

    private static HttpClient WithoutCookieJar(IdentityHttpHarness.ProductionHarness harness) =>
        harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

    private static async Task<string> SignInAsync(HttpClient client, string host, string email, string password = Password)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<Antiforgery> AntiforgeryAsync(HttpClient client, string host, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal))
            .Split(';')[0];
        return new Antiforgery(pair, token);
    }

    private sealed record Antiforgery(string Cookie, string Token);

    private sealed record CredentialsRow(bool HasPassword, DateTimeOffset? PasswordUpdatedAt);
}
