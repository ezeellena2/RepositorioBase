using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Credentials;

/// <summary>
/// Forgetting a password and changing one (IA-REQ-051, IA-REQ-049).
/// <para>
/// Everything goes through the production HTTP pipeline: what is being tested is what a person holding a mailed
/// link or a cookie can do, and both of those are transport facts.
/// </para>
/// </summary>
public sealed class PasswordLifecycleTests : TestBase
{
    private const string Password = "Testing1234!";
    private const string NewPassword = "Replaced5678!";

    private static string Host() => $"https://password-{Guid.NewGuid():N}.localhost";

    [Test]
    public async Task A_recovery_request_answers_the_same_whether_or_not_the_address_has_an_account()
    {
        var email = $"known-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();

        var known = await RecoverAsync(client, host, email);
        var unknown = await RecoverAsync(client, host, $"nobody-{Guid.NewGuid():N}@example.test");

        known.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted, "a neutral answer is the same answer either way");
        (await TestApp.CountAsync<PasswordResetRequest>()).ShouldBe(1, "only an address with an account has anything to reset");
        (await TestApp.ListAsync<OutboxMessage>()).Count(message => message.Type == "identity.password.recovery.requested").ShouldBe(1);
    }

    [Test]
    public async Task The_delivered_link_sets_a_new_password_and_ends_every_session()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var first = await SignInAsync(client, host, email, Password);
        var second = await SignInAsync(client, host, email, Password);
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = await DeliveredTokenAsync();

        var reset = await ResetAsync(client, host, token, NewPassword);

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        reset.Headers.Contains("Set-Cookie").ShouldBeFalse("a reset issues no session; the person signs in afterwards");
        (await ContextAsync(client, host, first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ContextAsync(client, host, second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TestApp.ListAsync<UserSession>()).ShouldAllBe(session => session.RevokedAt != null);
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "session.revoked" && entry.Metadata["outcome"] == "password_reset").ShouldBe(2);

        await SignInAsync(client, host, email, NewPassword);
        (await FailedSignInAsync(client, host, email, Password)).ShouldBeTrue("the old password stops working");
    }

    [Test]
    public async Task A_spent_reset_link_opens_nothing_the_second_time()
    {
        var email = $"replay-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = await DeliveredTokenAsync();
        (await ResetAsync(client, host, token, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replay = await ResetAsync(client, host, token, "Another9999!");

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(replay)).GetProperty("code").GetString().ShouldBe("invalid_credential_token");
        await SignInAsync(client, host, email, NewPassword);
    }

    [Test]
    public async Task Asking_again_replaces_the_previous_link_rather_than_leaving_two_that_work()
    {
        var email = $"reissue-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();

        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var firstToken = await DeliveredTokenAsync();
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var secondToken = await DeliveredTokenAsync();

        firstToken.ShouldNotBe(secondToken);
        (await ResetAsync(client, host, firstToken, NewPassword)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest, "the superseded link is dead, not merely older");
        (await ResetAsync(client, host, secondToken, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task A_reset_lifts_no_restriction_on_the_account()
    {
        var email = $"locked-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        await LockOutAsync(identityId);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = await DeliveredTokenAsync();

        (await ResetAsync(client, host, token, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The credential is replaced and the account is exactly as stopped as it was: recovering what you sign in
        // with is not a way around being stopped from signing in (amendment A3). Lockout is the restriction that
        // exists today; administrative suspension is C6's and arrives with Task 26.
        (await LockedOutAsync(identityId)).ShouldBeTrue();
        (await FailedSignInAsync(client, host, email, NewPassword)).ShouldBeTrue();
    }

    [Test]
    public async Task Changing_the_password_needs_a_recent_proof()
    {
        var email = $"unproved-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);

        var refused = await ChangeAsync(client, host, cookie, NewPassword);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        await SignInAsync(client, host, email, Password);
    }

    [Test]
    public async Task Changing_the_password_replaces_this_session_and_ends_the_others()
    {
        var email = $"change-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var other = await SignInAsync(client, host, email, Password);
        var current = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, current, "credentials.password.change");

        var changed = await ChangeAsync(client, host, current, NewPassword);

        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var replacement = changed.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
        replacement.ShouldNotBe(current, "the acting session is rotated into a new row that inherits nothing");
        (await ContextAsync(client, host, replacement)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ContextAsync(client, host, current)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ContextAsync(client, host, other)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(1);
        await SignInAsync(client, host, email, NewPassword);
    }

    [Test]
    public async Task A_password_the_policy_refuses_changes_nothing()
    {
        var email = $"weak-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, cookie, "credentials.password.change");

        var refused = await ChangeAsync(client, host, cookie, "short");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("validation_failed");
        (await ContextAsync(client, host, cookie)).StatusCode.ShouldBe(HttpStatusCode.OK, "the session that asked is untouched");
        await SignInAsync(client, host, email, Password);
    }

    [Test]
    public async Task Nothing_a_person_typed_reaches_a_log_an_audit_record_or_an_outbox_payload()
    {
        var email = $"quiet-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        TestApp.ResetCapturedLogs();
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = await DeliveredTokenAsync();
        (await ResetAsync(client, host, token, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var message in await TestApp.ListAsync<OutboxMessage>())
        {
            message.Payload.Contains(token, StringComparison.Ordinal).ShouldBeFalse("a usable token never enters a payload");
            message.Payload.Contains(email, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }

        foreach (var audit in await TestApp.ListAsync<AuditEvent>())
        {
            foreach (var value in audit.Metadata.Values)
            {
                value.Contains(token, StringComparison.Ordinal).ShouldBeFalse();
                value.Contains(NewPassword, StringComparison.Ordinal).ShouldBeFalse();
            }
        }

        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains(NewPassword, StringComparison.Ordinal));
        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains(token, StringComparison.Ordinal));
    }

    private static HttpClient WithoutCookieJar(IdentityHttpHarness.ProductionHarness harness) =>
        harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> RecoverAsync(HttpClient client, string host, string email)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/password/recovery", new { email }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string host, string token, string newPassword)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/password/reset", new { token, newPassword }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, string host, string cookie, string newPassword)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Put, $"{host}/api/identity/credentials/password", new { newPassword }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        return await client.SendAsync(request);
    }

    private static async Task ProveAsync(HttpClient client, string host, string cookie, string action)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/reauthenticate", new { action, password = Password }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> ContextAsync(HttpClient client, string host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/context");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<string> SignInAsync(HttpClient client, string host, string email, string password)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<bool> FailedSignInAsync(HttpClient client, string host, string email, string password)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        var response = await client.SendAsync(request);
        return !response.Headers.Contains("Set-Cookie");
    }

    private static async Task<Antiforgery> AntiforgeryAsync(HttpClient client, string host, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0];
        return new Antiforgery(pair, token);
    }

    /// <summary>The token the newest recovery message actually carries, read from its sealed envelope.</summary>
    private static async Task<string> DeliveredTokenAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages
            .Where(candidate => candidate.Type == "identity.password.recovery.requested")
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstAsync();
        var reader = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader>();
        return (await reader.ReadAsync(message.Id, CancellationToken.None))!;
    }

    private static async Task LockOutAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Users.Where(user => user.Id == identityId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LockoutEnabled, true)
                .SetProperty(user => user.LockoutEnd, DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static async Task<bool> LockedOutAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == identityId);
        return user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow;
    }

    private sealed record Antiforgery(string Cookie, string Token);
}
