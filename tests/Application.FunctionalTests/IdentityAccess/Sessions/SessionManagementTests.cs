using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Sessions;

/// <summary>
/// What an identity may do with its own sessions (IA-REQ-049).
/// <para>
/// Everything here goes through the production HTTP pipeline, because the thing under test is what a browser holding
/// a cookie can and cannot do — a request sent straight through MediatR would prove nothing about the cookie that
/// was just revoked.
/// </para>
/// </summary>
public sealed class SessionManagementTests : TestBase
{
    private const string Password = "Testing1234!";

    private static string Host() => $"https://sessions-{Guid.NewGuid():N}.localhost";

    [Test]
    public async Task Signing_in_a_second_time_leaves_the_first_session_signed_in()
    {
        var email = $"coexist-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();

        var first = await SignInAsync(client, host, email);
        var second = await SignInAsync(client, host, email);

        first.ShouldNotBe(second);
        (await ListAsync(client, host, first)).StatusCode.ShouldBe(HttpStatusCode.OK,
            "a sign-in on another device must not sign this one out");
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null)
            .ShouldBe(2, "both sessions are live");
    }

    [Test]
    public async Task An_identity_sees_only_its_own_sessions_and_which_one_it_is_using()
    {
        var email = $"lister-{Guid.NewGuid():N}@example.test";
        var stranger = $"stranger-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        await IdentityHttpHarness.SeedConfirmedUserAsync(stranger, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();

        await SignInAsync(client, host, stranger);
        var older = await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);

        var listed = await ReadSessionsAsync(client, host, current);

        listed.Length.ShouldBe(2, "only this identity's sessions, never the stranger's");
        listed.Count(session => session.IsCurrent).ShouldBe(1);
        listed[0].IsCurrent.ShouldBeTrue("the session in use leads the list");
        listed.Select(session => session.SessionRef).Distinct().Count().ShouldBe(2);
        listed.ShouldAllBe(session => session.DeviceLabel.Length > 0);
        older.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task A_listed_session_says_nothing_a_directory_could_be_built_from()
    {
        var email = $"quiet-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/sessions");
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "an empty body would make everything below pass for the wrong reason");
        var body = await response.Content.ReadAsStringAsync();
        var session = (await TestApp.ListAsync<UserSession>()).Single();

        body.Contains(session.Id.Value.ToString(), StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("the reference a client addresses a session by is never the identifier the cookie carries");
        body.Contains(email, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        body.Contains("Mozilla", StringComparison.OrdinalIgnoreCase).ShouldBeFalse("a raw user agent is never persisted or returned");
        body.Contains("127.0.0.1", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public async Task Revoking_another_session_ends_it_immediately_and_leaves_this_one_working()
    {
        var email = $"revoker-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var doomed = await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);
        var doomedRef = (await ReadSessionsAsync(client, host, current)).Single(session => !session.IsCurrent).SessionRef;
        await ProveAsync(client, host, current, "sessions.revoke-one");

        var revoked = await DeleteAsync(client, host, current, $"/api/identity/sessions/{doomedRef}");

        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client, host, doomed)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "the cookie of a revoked session stops working on the next request, not at its own expiry");
        (await ListAsync(client, host, current)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Revoking_a_session_that_is_already_gone_still_answers_that_it_is_gone()
    {
        var email = $"replay-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);
        var doomedRef = (await ReadSessionsAsync(client, host, current)).Single(session => !session.IsCurrent).SessionRef;

        await ProveAsync(client, host, current, "sessions.revoke-one");
        (await DeleteAsync(client, host, current, $"/api/identity/sessions/{doomedRef}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ProveAsync(client, host, current, "sessions.revoke-one");
        var again = await DeleteAsync(client, host, current, $"/api/identity/sessions/{doomedRef}");

        again.StatusCode.ShouldBe(HttpStatusCode.NoContent, "the requested end state holds either way");
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "session.revoked" && entry.Metadata.TryGetValue("outcome", out var outcome) && outcome == "revoked_by_owner")
            .ShouldBe(1, "one transition, one audit row");
    }

    [Test]
    public async Task A_reference_that_belongs_to_somebody_else_is_not_found_rather_than_refused()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var stranger = $"outsider-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        await IdentityHttpHarness.SeedConfirmedUserAsync(stranger, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var strangerCookie = await SignInAsync(client, host, stranger);
        var strangerRef = (await ReadSessionsAsync(client, host, strangerCookie)).Single().SessionRef;
        var current = await SignInAsync(client, host, email);
        await ProveAsync(client, host, current, "sessions.revoke-one");

        var refused = await DeleteAsync(client, host, current, $"/api/identity/sessions/{strangerRef}");

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("session_not_found");
        (await ListAsync(client, host, strangerCookie)).StatusCode.ShouldBe(HttpStatusCode.OK, "the stranger's session is untouched");
    }

    [Test]
    public async Task Revoking_the_others_keeps_exactly_the_session_that_asked()
    {
        var email = $"others-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var first = await SignInAsync(client, host, email);
        var second = await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);
        await ProveAsync(client, host, current, "sessions.revoke-others");

        (await DeleteAsync(client, host, current, "/api/identity/sessions/others")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client, host, first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ListAsync(client, host, second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadSessionsAsync(client, host, current)).Length.ShouldBe(1);

        await ProveAsync(client, host, current, "sessions.revoke-others");
        (await DeleteAsync(client, host, current, "/api/identity/sessions/others")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent, "a repeat with nothing left to revoke is still success");
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "session.revoked" && entry.Metadata.TryGetValue("outcome", out var outcome) && outcome == "revoked_by_owner")
            .ShouldBe(2, "two sessions ended, and the repeat ended none");
    }

    [Test]
    public async Task Revoking_a_session_the_caller_cannot_prove_it_still_is_is_refused()
    {
        var email = $"unproved-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);
        var doomedRef = (await ReadSessionsAsync(client, host, current)).Single(session => !session.IsCurrent).SessionRef;

        var refused = await DeleteAsync(client, host, current, $"/api/identity/sessions/{doomedRef}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(2, "nothing was revoked");
    }

    [Test]
    public async Task The_sixth_sign_in_ends_the_oldest_session_and_nothing_else()
    {
        var email = $"capped-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();

        var cookies = new List<string>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cookies.Add(await SignInAsync(client, host, email));
        }

        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null)
            .ShouldBe(5, "five live sessions, never six");
        (await ListAsync(client, host, cookies[0])).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized, "the oldest is the one that ends");
        (await ListAsync(client, host, cookies[1])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(client, host, cookies[5])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "session.revoked" && entry.Metadata.TryGetValue("outcome", out var outcome) && outcome == "evicted")
            .ShouldBe(1, "one eviction, audited once");
    }

    [Test]
    public async Task Signing_out_ends_only_the_session_that_asked()
    {
        var email = $"signout-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var other = await SignInAsync(client, host, email);
        var current = await SignInAsync(client, host, email);

        (await DeleteAsync(client, host, current, "/api/identity/sessions/current")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client, host, current)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ListAsync(client, host, other)).StatusCode.ShouldBe(HttpStatusCode.OK, "the other device stays signed in");
    }

    /// <summary>
    /// A client that stores no cookies. The shared harness client keeps a jar, so a request meant to carry a
    /// revoked session's cookie would silently be answered for the newest one instead — and every assertion about
    /// a revoked cookie would pass for the wrong reason.
    /// </summary>
    private static HttpClient WithoutCookieJar(IdentityHttpHarness.ProductionHarness harness) =>
        harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

    /// <summary>Signs in without a cookie jar, carrying the antiforgery pair by hand as a browser would.</summary>
    private static async Task<string> SignInAsync(HttpClient client, string host, string email)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = Password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<HttpResponseMessage> ListAsync(HttpClient client, string host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/sessions");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<SessionRow[]> ReadSessionsAsync(HttpClient client, string host, string cookie)
    {
        var response = await ListAsync(client, host, cookie);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionRow[]>())!;
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string host, string cookie, string path)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{host}{path}");
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        request.Headers.Add("Origin", host);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery.Token);
        return await client.SendAsync(request);
    }

    /// <summary>Spends a password to obtain the recent proof a sensitive session change requires (IA-REQ-051).</summary>
    private static async Task ProveAsync(HttpClient client, string host, string cookie, string action)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/reauthenticate", new { action, password = Password }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
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

    private sealed record SessionRow(string SessionRef, bool IsCurrent, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);
}
