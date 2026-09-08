using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.IdentityAccess.Security;
using Microsoft.Extensions.DependencyInjection;
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

    [Test]
    [Category("LoginControls")]
    public async Task The_platform_second_factor_budget_is_one_budget_across_two_independently_constructed_instances()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        const string host = "https://shared-mfa.localhost";
        const string wrong = "000000";

        // Three guesses answered by one deployment and two by another. The budget is five, and which instance a
        // load balancer picked is not something an attacker should be able to spend.
        using (var first = CreateProductionHarness())
        {
            var one = new PlatformOperator(first, host);
            await one.SignInAsync(OwnerEmail, PlatformScenario.ValidPassword);
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                (await one.PostAsync("/api/platform/mfa/step-up", new { code = wrong }))
                    .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"guess {attempt} is a wrong code, not a refusal");
            }
        }

        // The instance that counted the first three is gone, which is what a restart looks like from outside.
        using var second = CreateProductionHarness();
        var two = new PlatformOperator(second, host);
        await two.SignInAsync(OwnerEmail, PlatformScenario.ValidPassword);
        for (var attempt = 4; attempt <= 5; attempt++)
        {
            (await two.PostAsync("/api/platform/mfa/step-up", new { code = wrong }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"guess {attempt} is still inside the budget");
        }

        using var exhausted = await two.PostAsync("/api/platform/mfa/step-up", new { code = PlatformScenario.TotpCode(owner.SharedKey) });
        exhausted.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "the sixth guess is over the budget, right code or not");

        // And the counter is a row rather than a field: an adapter that never saw one of those requests finds the
        // budget spent. That is the only kind of counter a restart cannot refund.
        await AlreadySpentAsync(PlatformAttemptBudgets.MfaAttempt, PlatformAttemptBudgets.MfaKey(owner.IdentityId));
    }

    [Test]
    [Category("LoginControls")]
    public async Task The_bootstrap_recovery_budget_is_a_row_an_instance_that_never_saw_the_attempt_can_read()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var pending = await PlatformScenario.SingleInvitationAsync();
        PlatformScenario.RunAnonymously();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            (await TestApp.SendAsync(new RecoverPendingPlatformOwnerInvitationCommand())).IsSuccess
                .ShouldBeTrue($"attempt {attempt} of five is inside the budget");
        }

        var refused = await TestApp.SendAsync(new RecoverPendingPlatformOwnerInvitationCommand());
        refused.Error!.Code.ShouldBe("rate_limit_exceeded");

        await AlreadySpentAsync(PlatformAttemptBudgets.BootstrapRecovery, BootstrapKey(pending.Id.Value));
    }

    [Test]
    [Category("LoginControls")]
    public async Task The_personal_document_claim_budget_is_a_row_an_instance_that_never_saw_the_attempt_can_read()
    {
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync($"claimer-{Guid.NewGuid():N}@example.test", PlatformScenario.ValidPassword);
        PlatformScenario.RunAs(identityId);

        // Three claims is the budget C3 set, and it is the one scope Task 19 already put in the store. What was
        // never shown is that a second instance reads the same row, which is the whole reason it is there.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await TestApp.SendAsync(new CreatePersonalContextCommand("Claim Er", "Claim", $"{20000000 + attempt}"));
        }

        await AlreadySpentAsync(PersonalAttemptBudgets.DocumentClaim, identityId.ToString("N"));
    }

    /// <summary>
    /// Asks a freshly built adapter — a different object, a different connection, and one that answered none of
    /// the requests above — to spend one more attempt. A budget held in this process would admit it.
    /// </summary>
    private static async Task AlreadySpentAsync(AttemptBudget budget, string key)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var independent = new PostgreSqlAttemptBudget(
            scope.ServiceProvider.GetRequiredService<CleanArchitecture.Infrastructure.Data.ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(), FunctionalTestMetrics.Instance);

        var decision = await independent.SpendAsync(budget, key, CancellationToken.None);

        decision.Outcome.ShouldBe(
            AttemptBudgetOutcome.Exhausted,
            $"the {budget.Scope} budget has to be the row, not a field in whichever process answered");
    }

    private static string BootstrapKey(Guid pendingInvitationId) => $"{pendingInvitationId:N}|unknown";

    private const string OwnerEmail = "platform-owner@example.test";

    private static async Task<HttpResponseMessage> AttemptAsync(ProductionHarness harness, string host, string email, string client)
    {
        var antiforgery = await GetAntiforgeryAsync(harness.Client, host);
        using var request = LoginRequest(host, email, "not-a-secret", antiforgery, client);
        return await harness.Client.SendAsync(request);
    }
}
