using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task Review_a_credential_replacement_cannot_escape_the_version_read_order(bool resetAfterVersionCheck)
    {
        var email = $"review-credential-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        var barrier = new CredentialValidationBarrier();
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            if (resetAfterVersionCheck)
            {
                services.RemoveAll<ISessionIssuer>();
                services.AddScoped<ISessionIssuer>(provider => new PausedSessionIssuer(
                    ActivatorUtilities.CreateInstance<SessionIssuer>(provider), barrier));
                services.RemoveAll<ISessionLock>();
                services.AddScoped<ISessionLock>(provider => new ObservedSessionLock(
                    ActivatorUtilities.CreateInstance<SessionLock>(provider), barrier));
            }
            else
            {
                services.RemoveAll<IIdentityAccountService>();
                services.AddScoped<IIdentityAccountService>(provider => new PausedIdentityAccountService(
                    ActivatorUtilities.CreateInstance<IdentityAccountService>(provider), barrier, pauseAfterLookup: true));
            }
        });
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var current = await SignInAsync(client, host, email, Password);
        string? token = null;
        if (resetAfterVersionCheck)
        {
            (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            token = await DeliveredTokenAsync();
        }
        else await ProveAsync(client, host, current, "credentials.password.change");

        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = Password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        barrier.Arm();
        var pendingLogin = client.SendAsync(request);
        Task<HttpResponseMessage>? replacement = null;
        try
        {
            await barrier.Validated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            replacement = resetAfterVersionCheck
                ? ResetAsync(client, host, token!, NewPassword)
                : ChangeAsync(client, host, current, NewPassword);
            if (resetAfterVersionCheck)
            {
                // A correct reset may wait for the login's identity lock. Release the login once reset either
                // tries that lock or commits without it; never demand an unsafe ordering from a future fix.
                await Task.WhenAny(replacement, barrier.CompetingLockAttempt.Task).WaitAsync(TimeSpan.FromSeconds(30));
            }
            else await replacement.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { barrier.Release.TrySetResult(); }

        using var login = await pendingLogin.WaitAsync(TimeSpan.FromSeconds(30));
        using var changed = await replacement!.WaitAsync(TimeSpan.FromSeconds(30));
        var lateCookie = login.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))?.Split(';')[0]
            : null;
        using var protectedResponse = await ContextAsync(client, host, lateCookie ?? string.Empty);
        TestContext.Out.WriteLine($"Review R1A afterVersionReset={resetAfterVersionCheck}; credential change={(int)changed.StatusCode}; login={(int)login.StatusCode}; cookie={lateCookie is not null}; protected GET={(int)protectedResponse.StatusCode}; reset requested lock={barrier.CompetingLockAttempt.Task.IsCompleted}");
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        protectedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a password replacement must either prevent this old-password session or revoke it before the replacement commits");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Revalidation_a_login_validated_before_a_password_change_cannot_open_a_session_after_it(bool useRecovery)
    {
        var email = $"credential-race-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        var barrier = new CredentialValidationBarrier();
        using var harness = IdentityHttpHarness.CreateProductionHarness(configureTestServices: services =>
        {
            services.RemoveAll<IIdentityAccountService>();
            services.AddScoped<IIdentityAccountService>(provider => new PausedIdentityAccountService(
                ActivatorUtilities.CreateInstance<IdentityAccountService>(provider), barrier));
        });
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var current = await SignInAsync(client, host, email, Password);
        string? resetToken = null;
        if (useRecovery)
        {
            (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            resetToken = await DeliveredTokenAsync();
        }
        else await ProveAsync(client, host, current, "credentials.password.change");

        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password = Password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        barrier.Arm();
        var pendingLogin = client.SendAsync(request);
        HttpResponseMessage changed;
        try
        {
            await barrier.Validated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            changed = useRecovery
                ? await ResetAsync(client, host, resetToken!, NewPassword)
                : await ChangeAsync(client, host, current, NewPassword);
        }
        finally { barrier.Release.TrySetResult(); }

        using var login = await pendingLogin.WaitAsync(TimeSpan.FromSeconds(30));
        var lateCookie = login.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))?.Split(';')[0]
            : null;
        using var protectedResponse = await ContextAsync(client, host, lateCookie ?? string.Empty);
        TestContext.Out.WriteLine($"recovery={useRecovery}; credential change={(int)changed.StatusCode}; late login={(int)login.StatusCode}; late cookie={lateCookie is not null}; protected GET={(int)protectedResponse.StatusCode}");
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        protectedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "credentials invalidated before session issuance must never produce a usable authenticated session (C2/C4)");
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        lateCookie.ShouldBeNull();
    }

    [Test]
    public async Task Revalidation_a_recovery_link_issued_before_an_authenticated_password_change_is_invalid()
    {
        var email = $"old-reset-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var current = await SignInAsync(client, host, email, Password);
        (await RecoverAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var oldToken = await DeliveredTokenAsync();
        await ProveAsync(client, host, current, "credentials.password.change");
        using var changed = await ChangeAsync(client, host, current, NewPassword);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var replacement = changed.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];

        const string RecoveredPassword = "OldLinkRejected999!";
        using var reset = await ResetAsync(client, host, oldToken, RecoveredPassword);
        using var protectedResponse = await ContextAsync(client, host, replacement);
        var changedPasswordRejected = await FailedSignInAsync(client, host, email, NewPassword);
        var resetPasswordRejected = await FailedSignInAsync(client, host, email, RecoveredPassword);
        TestContext.Out.WriteLine($"old reset={(int)reset.StatusCode}; replacement session={(int)protectedResponse.StatusCode}; changed password rejected={changedPasswordRejected}; reset password rejected={resetPasswordRejected}");
        reset.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "an earlier reset link must no longer match the identity security version (C4)");
        (await IdentityHttpHarness.ReadProblemAsync(reset)).GetProperty("code").GetString().ShouldBe("invalid_credential_token");
        protectedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        changedPasswordRejected.ShouldBeFalse();
        resetPasswordRejected.ShouldBeTrue();
    }

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
        const string submittedPassword = "tiny";
        var email = $"weak-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, cookie, "credentials.password.change");

        var refused = await ChangeAsync(client, host, cookie, submittedPassword);

        var payload = await IdentityHttpHarness.AssertProblemAsync(
            refused,
            HttpStatusCode.BadRequest,
            "validation_failed",
            hasErrors: true);
        payload.GetProperty("instance").GetString().ShouldBe("/api/identity/credentials/password");
        payload.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["newPassword"]);
        var details = payload.GetProperty("errors").GetProperty("newPassword").EnumerateArray().ToArray();
        details.Select(detail => detail.GetProperty("code").GetString()).ShouldBe([
            ValidationErrorCodes.PasswordRequiresDigit,
            ValidationErrorCodes.PasswordRequiresSymbol,
            ValidationErrorCodes.PasswordRequiresUppercase,
            ValidationErrorCodes.PasswordTooShort,
        ]);
        details.ShouldAllBe(detail =>
            detail.ValueKind == System.Text.Json.JsonValueKind.Object
            && detail.EnumerateObject().Select(property => property.Name).SequenceEqual(new[] { "code", "params" }));
        details.Single(detail => detail.GetProperty("code").GetString() == ValidationErrorCodes.PasswordTooShort)
            .GetProperty("params").GetProperty("min").GetInt32().ShouldBe(12);
        details.Where(detail => detail.GetProperty("code").GetString() != ValidationErrorCodes.PasswordTooShort)
            .ShouldAllBe(detail => !detail.GetProperty("params").EnumerateObject().Any());
        payload.GetRawText().ShouldNotContain(submittedPassword, Case.Insensitive);
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
        // A refused sign-in leaves no cookie, and asking what that non-cookie can reach is the point of the
        // question. An empty header value is rejected by HttpClient, so it is simply not sent.
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
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

    private sealed class CredentialValidationBarrier
    {
        private int _armed;
        internal TaskCompletionSource Validated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CompetingLockAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);
        internal async Task PauseOnceAsync()
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1) return;
            Validated.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class PausedSessionIssuer(ISessionIssuer inner, CredentialValidationBarrier barrier) : ISessionIssuer
    {
        public async Task<UserSession?> IssueAsync(Guid identityId, string correlationId, CancellationToken cancellationToken)
        {
            await barrier.PauseOnceAsync();
            return await inner.IssueAsync(identityId, correlationId, cancellationToken);
        }
    }

    private sealed class ObservedSessionLock(ISessionLock inner, CredentialValidationBarrier barrier) : ISessionLock
    {
        public Task<bool> TryAcquireAsync(Guid identityId, CancellationToken cancellationToken)
        {
            if (barrier.Validated.Task.IsCompleted && !barrier.Release.Task.IsCompleted)
                barrier.CompetingLockAttempt.TrySetResult();
            return inner.TryAcquireAsync(identityId, cancellationToken);
        }
    }

    private sealed class PausedIdentityAccountService(IIdentityAccountService inner, CredentialValidationBarrier barrier, bool pauseAfterLookup = false) : IIdentityAccountService
    {
        public async Task<IdentityAccount?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken)
        {
            var account = await inner.ValidateCredentialsAsync(email, password, cancellationToken);
            if (account is not null && !pauseAfterLookup) await barrier.PauseOnceAsync();
            return account;
        }
        public async Task<IdentityAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
        {
            var account = await inner.FindByEmailAsync(email, cancellationToken);
            if (account is not null && pauseAfterLookup) await barrier.PauseOnceAsync();
            return account;
        }
        public Task<IdentityAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => inner.FindByIdAsync(id, cancellationToken);
        public Task<IdentityAccountValidationResult> ValidatePendingRegistrationAsync(string email, string password, CancellationToken cancellationToken) => inner.ValidatePendingRegistrationAsync(email, password, cancellationToken);
        public Task<IdentityAccountValidationResult> ValidatePasswordAsync(string password, CancellationToken cancellationToken) => inner.ValidatePasswordAsync(password, cancellationToken);
        public Task<IdentityAccountCreationResult> CreatePendingAsync(string email, string password, string preferredLanguage, CancellationToken cancellationToken) => inner.CreatePendingAsync(email, password, preferredLanguage, cancellationToken);
        public string HashPassword(string password) => inner.HashPassword(password);
        public Task<IdentityAccountCreationResult> CreatePendingFromHashAsync(string email, string hash, string preferredLanguage, CancellationToken cancellationToken) => inner.CreatePendingFromHashAsync(email, hash, preferredLanguage, cancellationToken);
        public Task ActivateAsync(Guid id, CancellationToken cancellationToken) => inner.ActivateAsync(id, cancellationToken);
        public Task<bool> SetPreferredLanguageAsync(Guid id, string language, CancellationToken cancellationToken) => inner.SetPreferredLanguageAsync(id, language, cancellationToken);
        public Task<bool> VerifyPasswordAsync(Guid id, string password, CancellationToken cancellationToken) => inner.VerifyPasswordAsync(id, password, cancellationToken);
        public Task<bool> TryTransitionAsync(Guid id, CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus expected, CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus next, CancellationToken cancellationToken) =>
            inner.TryTransitionAsync(id, expected, next, cancellationToken);
    }
}
