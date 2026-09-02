using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Sessions;

/// <summary>
/// The five-failure lockout is a durable count, not a best-effort one (IA-REQ-019). Parallel failed sign-ins of the
/// same account each leave a persisted attempt, so the fifth locks the account for exactly fifteen minutes of the
/// injected clock, and every response stays the neutral one an unknown account receives (IA-REQ-029).
/// </summary>
[Category("LoginControls")]
public sealed class LoginLockoutConcurrencyTests : TestBase
{
    private const string Password = "Testing1234!";
    private const string WrongPassword = "Wrong-password-1!";

    [Test]
    public async Task Five_simultaneous_failed_sign_ins_lock_the_account_exactly_at_the_fifth_attempt()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 7, 1, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        const string host = "https://login-parallel-lockout.localhost";
        const string email = "parallel-lockout@example.test";
        var identityId = await SeedConfirmedUserAsync(email, Password);
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableFailedAccessBarrier(5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async attempt =>
        {
            using var request = LoginRequest(host, email, WrongPassword, antiforgery, $"203.0.113.{110 + attempt}");
            var response = await client.SendAsync(request);
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync(), HasCookie: response.Headers.TryGetValues("Set-Cookie", out _));
        }));

        TestApp.FailedAccessBarrierWasFullyObserved.ShouldBeTrue("all five failures must contend for the same account row");
        foreach (var (statusCode, body, hasCookie) in responses)
        {
            statusCode.ShouldBe(HttpStatusCode.NoContent, "every failure keeps the neutral credential response");
            body.ShouldBeEmpty();
            hasCookie.ShouldBeFalse();
        }

        var locked = await GetUserAsync(identityId);
        locked.LockoutEnd.ShouldBe(clock.GetUtcNow().AddMinutes(15), "exactly five persisted failures lock the account");
        locked.AccessFailedCount.ShouldBe(0, "the lockout starts a fresh failure window");
        (await CountAsync<UserSession>()).ShouldBe(0);
        (await ListAsync<AuditEvent>()).Count(item => item.EventType == "signin.failed").ShouldBe(5, "every attempt is audited exactly once");

        using (var duringLockout = LoginRequest(host, email, Password, antiforgery, "203.0.113.120"))
        {
            var response = await client.SendAsync(duringLockout);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, "a locked account is indistinguishable from wrong credentials");
            (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
            response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
        }

        clock.Advance(TimeSpan.FromMinutes(15).Subtract(TimeSpan.FromSeconds(1)));
        using (var beforeExpiry = LoginRequest(host, email, Password, antiforgery, "203.0.113.121"))
        {
            var response = await client.SendAsync(beforeExpiry);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse("the lockout still holds one second before it ends");
        }

        (await CountAsync<UserSession>()).ShouldBe(0);
        (await GetUserAsync(identityId)).LockoutEnd.ShouldBe(locked.LockoutEnd, "a rejected attempt never extends the lockout");

        clock.Advance(TimeSpan.FromSeconds(1));
        using var afterExpiry = LoginRequest(host, email, Password, antiforgery, "203.0.113.122");
        var recovered = await client.SendAsync(afterExpiry);

        recovered.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        recovered.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal));
        (await CountAsync<UserSession>()).ShouldBe(1);
        (await GetOnlySessionAsync()).IdentityId.ShouldBe(identityId);
        (await GetUserAsync(identityId)).AccessFailedCount.ShouldBe(0);
    }

    [Test]
    public async Task Four_simultaneous_failed_sign_ins_are_all_counted_without_locking_the_account()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 7, 2, 0, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var client = harness.Client;
        const string host = "https://login-parallel-below-lockout.localhost";
        const string email = "parallel-below-lockout@example.test";
        var identityId = await SeedConfirmedUserAsync(email, Password);
        var antiforgery = await GetAntiforgeryAsync(client, host);
        TestApp.EnableFailedAccessBarrier(4);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(async attempt =>
        {
            using var request = LoginRequest(host, email, WrongPassword, antiforgery, $"203.0.113.{130 + attempt}");
            return (await client.SendAsync(request)).StatusCode;
        }));

        TestApp.FailedAccessBarrierWasFullyObserved.ShouldBeTrue("all four failures must contend for the same account row");
        statuses.ShouldAllBe(status => status == HttpStatusCode.NoContent);
        var user = await GetUserAsync(identityId);
        user.AccessFailedCount.ShouldBe(4, "no failure may be dropped by a lost update");
        user.LockoutEnd.ShouldBeNull("four failures are one short of the lockout threshold");

        using var fifth = LoginRequest(host, email, WrongPassword, antiforgery, "203.0.113.140");
        (await client.SendAsync(fifth)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var lockedOut = await GetUserAsync(identityId);
        lockedOut.LockoutEnd.ShouldBe(clock.GetUtcNow().AddMinutes(15));
        lockedOut.AccessFailedCount.ShouldBe(0);
    }
}
