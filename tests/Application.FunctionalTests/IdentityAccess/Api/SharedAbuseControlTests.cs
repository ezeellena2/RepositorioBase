using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// What a login budget is worth once the deployment is more than one process (IA-REQ-019, IA-REQ-057).
/// <para>
/// Every test here builds its hosts <em>independently</em> — separate service providers, separate middleware
/// pipelines, separate everything except the one PostgreSQL database they were pointed at. That is the whole
/// point: a budget held in a field is a budget an attacker escapes by reaching the other instance, and a suite
/// that only ever asks one process cannot tell the two designs apart. A restart is modelled the same honest way,
/// by disposing the host that spent the attempts and building another.
/// </para>
/// <para>
/// The clock is injected and identical across instances, so nothing here depends on wall time and the windows are
/// the ones the policy names rather than the ones a slow machine produced.
/// </para>
/// </summary>
public sealed class SharedAbuseControlTests : TestBase
{
    private static readonly DateTimeOffset Now = new(2031, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Test]
    [Category("LoginControls")]
    public async Task The_login_client_budget_is_one_budget_across_two_independently_constructed_instances()
    {
        const string host = "https://shared-login-client.localhost";
        const string client = "198.51.100.7";
        using var first = CreateProductionHarness(new ControlledTimeProvider(Now));
        using var second = CreateProductionHarness(new ControlledTimeProvider(Now));

        // Twenty is the budget for a client address. Split across two instances it is still twenty, because the
        // attacker chooses which instance answers and would otherwise simply alternate.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            (await AttemptAsync(first, host, $"first-{attempt}@example.test", client))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} on the first instance is inside the budget");
        }

        for (var attempt = 11; attempt <= 20; attempt++)
        {
            (await AttemptAsync(second, host, $"second-{attempt}@example.test", client))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} on the second instance is inside the same budget");
        }

        var refusedBySecond = await AttemptAsync(second, host, "twenty-first@example.test", client);
        refusedBySecond.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "the twenty-first attempt is over the budget whichever instance sees it");
        var refusedByFirst = await AttemptAsync(first, host, "twenty-second@example.test", client);
        refusedByFirst.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "and the instance that spent the first ten agrees");
    }

    [Test]
    [Category("LoginControls")]
    public async Task The_login_account_budget_survives_the_restart_of_the_instance_that_spent_it()
    {
        const string host = "https://shared-login-restart.localhost";
        const string account = "restart-target@example.test";

        using (var before = CreateProductionHarness(new ControlledTimeProvider(Now)))
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                (await AttemptAsync(before, host, account, $"198.51.100.{100 + attempt}"))
                    .StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt} of ten is inside the account budget");
            }
        }

        // The process that counted them is gone. A budget an attacker clears by waiting for a deployment is not a
        // budget, so the eleventh attempt has to meet the same answer from a host that never saw the first ten.
        using var after = CreateProductionHarness(new ControlledTimeProvider(Now));
        var refused = await AttemptAsync(after, host, account, "198.51.100.200");
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "restarting an instance does not refund an attempt");
    }

    [Test]
    [Category("LoginControls")]
    public async Task An_unreachable_budget_store_refuses_the_login_as_an_outage_rather_than_as_too_many_attempts()
    {
        const string host = "https://shared-login-outage.localhost";
        using var harness = CreateProductionHarness(new ControlledTimeProvider(Now));
        TestApp.ForceAttemptBudgetUnavailable();

        var response = await AttemptAsync(harness, host, "outage@example.test", "198.51.100.30");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, "a caller who spent nothing must not be told they spent everything");
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("service_unavailable");
        response.Headers.RetryAfter!.Delta!.Value.ShouldBe(TimeSpan.FromSeconds(30));
        (await CountAsync<CleanArchitecture.Domain.IdentityAccess.Sessions.UserSession>())
            .ShouldBe(0, "failing closed means the credential is never checked at all");
    }

    private static async Task<HttpResponseMessage> AttemptAsync(ProductionHarness harness, string host, string email, string client)
    {
        var antiforgery = await GetAntiforgeryAsync(harness.Client, host);
        using var request = LoginRequest(host, email, "not-a-secret", antiforgery, client);
        return await harness.Client.SendAsync(request);
    }
}
