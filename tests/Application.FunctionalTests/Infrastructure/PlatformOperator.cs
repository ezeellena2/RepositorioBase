using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// One person driving the real routes with a cookie of their own.
/// <para>
/// It keeps no cookie jar on purpose: several tests need two people, or one person on two devices, in flight at
/// once, and a jar would quietly merge them into a single session. Every request therefore carries its cookies
/// explicitly, the way a browser would.
/// </para>
/// </summary>
internal sealed class PlatformOperator : IAsyncDisposable
{
    private readonly IdentityHttpHarness.ProductionHarness _harness;
    private readonly HttpClient _client;
    private readonly string _host;
    private string? _session;

    internal PlatformOperator(IdentityHttpHarness.ProductionHarness harness, string host)
    {
        _harness = harness;
        _host = host;
        _client = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
    }

    internal async Task SignInAsync(string email, string password)
    {
        _session = await TrySignInAsync(email, password)
            ?? throw new InvalidOperationException($"The sign-in this test needs as its premise was refused for {email}.");
    }

    /// <summary>Whether this address and password still open a session, asked through the front door.</summary>
    internal async Task<bool> CanSignInAsync(string email, string password) =>
        await TrySignInAsync(email, password) is not null;

    internal async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_host}{path}");
        if (_session is not null) request.Headers.Add("Cookie", _session);
        return await _client.SendAsync(request);
    }

    internal Task<HttpResponseMessage> PostAsync(string path, object? body) => SendAsync(HttpMethod.Post, path, body);

    internal Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
    {
        var antiforgery = await AntiforgeryAsync(_session);
        using var request = IdentityHttpHarness.JsonRequest(method, $"{_host}{path}", body, antiforgery.Token);
        request.Headers.Add("Cookie", _session is null ? antiforgery.Cookie : $"{_session}; {antiforgery.Cookie}");
        return await _client.SendAsync(request);
    }

    private async Task<string?> TrySignInAsync(string email, string password)
    {
        var antiforgery = await AntiforgeryAsync(null);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post, $"{_host}/api/identity/sessions", new { email, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        using var response = await _client.SendAsync(request);
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        return cookies.FirstOrDefault(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))?.Split(';')[0];
    }

    private async Task<(string Cookie, string Token)> AntiforgeryAsync(string? session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_host}/api/identity/antiforgery");
        if (session is not null) request.Headers.Add("Cookie", session);
        using var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0];
        return (pair, token);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _harness.Dispose();
        return ValueTask.CompletedTask;
    }
}
