using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// Getting a second factor back after losing the authenticator that held it (IA-REQ-041, IA-REQ-054).
/// <para>
/// The bar this route must clear: it is the only way to replace a working factor without proving that factor, so
/// everything else about the caller has to be true at once — their session, their password proved a moment ago,
/// and one recovery code nobody has spent. Holding the code alone is not enough, and neither is holding the
/// password.
/// </para>
/// </summary>
public sealed class PlatformMfaRecoveryTests : TestBase
{
    private const string OwnerEmail = "platform-owner@example.test";

    [Test]
    public async Task Losing_the_authenticator_is_not_losing_the_account()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var codes = await EnterRecoveryAsync(owner);

        var recovered = await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[0]));

        recovered.IsSuccess.ShouldBeTrue(recovered.Error?.Code);
        recovered.Value!.SharedKey.ShouldNotBe(owner.SharedKey, "a replaced factor is a different secret");
        recovered.Value.RecoveryCodes.ShouldNotBeEmpty();
        recovered.Value.RecoveryCodes.ShouldNotContain(codes[0], "the codes the recovery hands over are new ones");

        // The authenticator that was lost stops working, and the one just handed over starts.
        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(owner.SharedKey)))).IsFailure.ShouldBeTrue();
        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(recovered.Value.SharedKey)))).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Until the replacement is proved, nobody has proved anything — and this asks it of the very session that
    /// had just proved the old factor, because that is the only session for which the answer could be wrong. A
    /// recovery that left its step-up standing would hand Platform authority to a caller who showed a code from
    /// a piece of paper and nothing else.
    /// </summary>
    [Test]
    public async Task The_replacement_factor_has_not_been_proved_by_anybody_yet()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        await SignInForRealAsync(owner);
        TestApp.SetCurrentTenant(owner.PlatformId);

        // This session proves the factor it still has, and can therefore change Platform.
        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(owner.SharedKey)))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new InvitePlatformAdministratorCommand("before-recovery@example.test"))).IsSuccess
            .ShouldBeTrue("the premise of this test is a session that could change Platform a moment ago");

        await ProveAsync();
        var recovered = await TestApp.SendAsync(new RecoverPlatformMfaCommand(owner.RecoveryCodes[0]));
        recovered.IsSuccess.ShouldBeTrue(recovered.Error?.Code);

        var change = await TestApp.SendAsync(new InvitePlatformAdministratorCommand("after-recovery@example.test"));
        change.IsFailure.ShouldBeTrue("replacing the factor takes the freshness of the one it replaced with it");
        change.Error!.Code.ShouldBe("recent_mfa_required");

        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(recovered.Value!.SharedKey)))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new InvitePlatformAdministratorCommand("after-recovery@example.test"))).IsSuccess.ShouldBeTrue();
    }

    [Test]
    public async Task A_code_that_has_been_spent_is_worth_nothing_a_second_time()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var codes = await EnterRecoveryAsync(owner);
        (await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[0]))).IsSuccess.ShouldBeTrue();

        await ProveAsync();
        var replayed = await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[0]));

        replayed.IsFailure.ShouldBeTrue();
        replayed.Error!.Code.ShouldBe("invalid_credential_proof");
    }

    /// <summary>
    /// Every other code from the same set goes with the factor it belonged to. Keeping them alive would mean a
    /// recovery hands over a new set while the old one still opens the account.
    /// </summary>
    [Test]
    public async Task The_codes_that_belonged_to_the_replaced_factor_go_with_it()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var codes = await EnterRecoveryAsync(owner);
        codes.Count.ShouldBeGreaterThan(1, "this test needs a second code to still be unspent");
        (await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[0]))).IsSuccess.ShouldBeTrue();

        await ProveAsync();
        var stale = await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[1]));

        stale.IsFailure.ShouldBeTrue();
        stale.Error!.Code.ShouldBe("invalid_credential_proof");
    }

    [Test]
    public async Task A_recovery_needs_the_password_proved_a_moment_ago()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        await SignInForRealAsync(owner);

        var refused = await TestApp.SendAsync(new RecoverPlatformMfaCommand(owner.RecoveryCodes[0]));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("recent_proof_required");
        await FactorIsUnchangedAsync();
    }

    /// <summary>
    /// A proof bought for something else is not a proof for this. The action set is closed precisely so that
    /// proving in order to change a password is not permission to replace a second factor.
    /// </summary>
    [Test]
    public async Task A_proof_bought_for_another_action_does_not_pay_for_this_one()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        await SignInForRealAsync(owner);
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PasswordChange, PlatformScenario.ValidPassword))).IsSuccess.ShouldBeTrue();

        var refused = await TestApp.SendAsync(new RecoverPlatformMfaCommand(owner.RecoveryCodes[0]));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("recent_proof_required");
        await FactorIsUnchangedAsync();
    }

    /// <summary>
    /// Being inside the Platform tenant is not a bar, and must not be: signing in selects the only tenant an
    /// operator belongs to, so the person who lost their authenticator is sitting in it by the time they ask.
    /// A route that refused there would be a route nobody who needs it can reach.
    /// </summary>
    [Test]
    public async Task Being_signed_into_Platform_is_not_a_bar_because_that_is_where_the_person_already_is()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var codes = await EnterRecoveryAsync(owner);
        TestApp.SetCurrentTenant(owner.PlatformId);

        var recovered = await TestApp.SendAsync(new RecoverPlatformMfaCommand(codes[0]));

        recovered.IsSuccess.ShouldBeTrue(recovered.Error?.Code);
        recovered.Value!.SharedKey.ShouldNotBe(owner.SharedKey);
    }

    /// <summary>
    /// A factor that was never completed cannot be recovered, because there is nothing to recover: the enrollment
    /// gates exist to be walked, and this would be a way round them.
    /// </summary>
    [Test]
    public async Task A_factor_nobody_ever_finished_setting_up_is_not_recoverable()
    {
        var invitation = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.RegisterPlatformInviteeCommand(invitation.Token, PlatformScenario.ValidPassword));
        var confirmation = await PlatformScenario.SealedTokenAsync(
            (await PlatformScenario.MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.ConfirmPlatformInviteeCommand(confirmation));

        var identityId = await IdentityIdAsync(invitation.Email);
        PlatformScenario.RunAs(identityId);
        var begun = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(invitation.Token));
        begun.IsSuccess.ShouldBeTrue();

        // A real session, because a proof is a row that names the session it was produced in.
        PlatformScenario.RunAnonymously();
        var signedIn = await TestApp.SendAsync(new CreateSessionCommand(invitation.Email, PlatformScenario.ValidPassword));
        signedIn.IsSuccess.ShouldBeTrue(signedIn.Error?.Code);
        TestApp.SetUserId(identityId);
        TestApp.SetSessionId(signedIn.Value!.SessionId);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PlatformMfaRecover, PlatformScenario.ValidPassword))).IsSuccess.ShouldBeTrue();

        var refused = await TestApp.SendAsync(new RecoverPlatformMfaCommand(begun.Value!.RecoveryCodes[0]));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("invalid_credential_proof");
    }

    /// <summary>
    /// The same budget as verifying and stepping up, because it is the same account and the same guess. A limit on
    /// two of the three routes would only move the guessing to the third.
    /// </summary>
    [Test]
    public async Task Guessing_a_recovery_code_meets_the_budget_guessing_the_factor_meets()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        await SignInForRealAsync(owner);

        string? exhausted = null;
        var retryAfter = 0;
        for (var attempt = 0; attempt < 25 && exhausted is null; attempt++)
        {
            await ProveAsync();
            var refused = await TestApp.SendAsync(new RecoverPlatformMfaCommand($"not-a-code-{attempt}"));
            refused.IsFailure.ShouldBeTrue();
            if (refused.Error!.Code == "rate_limit_exceeded")
            {
                exhausted = refused.Error.Code;
                retryAfter = refused.Error.RetryAfterSeconds ?? 0;
            }
        }

        exhausted.ShouldBe("rate_limit_exceeded", "an unbounded recovery route is a guessing game with a fixed cost");
        retryAfter.ShouldBeGreaterThan(0, "a caller told to wait has to be told how long");
        await FactorIsUnchangedAsync();
    }

    /// <summary>
    /// The same person on two devices, each signed in, each holding its own proof, each spending its own code.
    /// A proof is single-use per session, so this is the only shape the race actually has — and it is a real one:
    /// one factor exists afterwards, and the device that lost is told the row moved rather than walking away with
    /// a secret that is already stale.
    /// <para>
    /// It goes over HTTP because that is the only place two sessions of one identity can be in flight at once:
    /// each request carries its own cookie, where the in-process harness has a single ambient session.
    /// </para>
    /// </summary>
    [Test]
    public async Task Two_recoveries_at_once_leave_exactly_one_factor()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        owner.RecoveryCodes.Count.ShouldBeGreaterThan(1);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var host = $"https://mfa-recovery-{Guid.NewGuid():N}.localhost";

        var phone = await SignInAndProveAsync(client, host);
        var laptop = await SignInAndProveAsync(client, host);

        var first = RecoverOverHttpAsync(client, host, phone, owner.RecoveryCodes[0]);
        var second = RecoverOverHttpAsync(client, host, laptop, owner.RecoveryCodes[1]);
        using var firstResponse = await first;
        using var secondResponse = await second;

        var responses = new[] { firstResponse, secondResponse };
        responses.Count(response => response.StatusCode == HttpStatusCode.OK)
            .ShouldBe(1, "two recoveries must not both replace the factor");
        var loser = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(loser)).GetProperty("code").GetString()
            .ShouldBe("platform_mfa_concurrency_conflict");

        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var replacement = await winner.Content.ReadFromJsonAsync<CleanArchitecture.Web.PlatformEndpoints.Contracts.PlatformMfaEnrollmentResponse>();
        await SignInForRealAsync(owner);
        (await TestApp.SendAsync(new StepUpPlatformMfaCommand(PlatformScenario.TotpCode(replacement!.SharedKey)))).IsSuccess
            .ShouldBeTrue("the factor that exists is the one the winner was handed");
    }

    /// <summary>One browser: a session of its own, and a proof of its own bought for this action.</summary>
    private static async Task<string> SignInAndProveAsync(HttpClient client, string host)
    {
        var cookie = await SignInOverHttpAsync(client, host);
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post,
            $"{host}/api/identity/credentials/reauthenticate",
            new { action = ProofActions.PlatformMfaRecover, password = PlatformScenario.ValidPassword },
            antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return cookie;
    }

    /// <summary>
    /// A sign-in that carries its own antiforgery pair, because this client keeps no cookie jar — which is the
    /// whole point: two browsers, two cookies, two sessions of the same person.
    /// </summary>
    private static async Task<string> SignInOverHttpAsync(HttpClient client, string host)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post, $"{host}/api/identity/sessions",
            new { email = OwnerEmail, password = PlatformScenario.ValidPassword }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<HttpResponseMessage> RecoverOverHttpAsync(HttpClient client, string host, string cookie, string code)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(
            HttpMethod.Post, $"{host}/api/platform/mfa/recover", new { recoveryCode = code }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        return await client.SendAsync(request);
    }

    private static async Task<(string Cookie, string Token)> AntiforgeryAsync(HttpClient client, string host, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/antiforgery");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await response.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var pair = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0];
        return (pair, token);
    }

    /// <summary>
    /// Proves the password for this action and hands back the codes that are still unspent. Leaving the Platform
    /// context first is what a person who lost their authenticator would be doing anyway.
    /// </summary>
    /// <summary>
    /// Where somebody who lost their authenticator actually is: signed in for real, in no tenant, holding a
    /// password they have just re-typed. The ceremony's session is a synthetic one with no row behind it, and a
    /// proof is a row that names its session — so this route cannot be reached from it.
    /// </summary>
    private static async Task<IReadOnlyList<string>> EnterRecoveryAsync(PlatformScenario.ActiveOwner owner)
    {
        await SignInForRealAsync(owner);
        await ProveAsync();
        return owner.RecoveryCodes;
    }

    private static async Task SignInForRealAsync(PlatformScenario.ActiveOwner owner)
    {
        PlatformScenario.RunAnonymously();
        var created = await TestApp.SendAsync(new CreateSessionCommand(OwnerEmail, PlatformScenario.ValidPassword));
        created.IsSuccess.ShouldBeTrue(created.Error?.Code);
        TestApp.SetUserId(owner.IdentityId);
        TestApp.SetSessionId(created.Value!.SessionId);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
    }

    private static async Task ProveAsync() =>
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PlatformMfaRecover, PlatformScenario.ValidPassword)))
            .IsSuccess.ShouldBeTrue();

    private static async Task FactorIsUnchangedAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var enrollment = await context.PlatformMfaEnrollments.AsNoTracking()
            .Include(candidate => candidate.RecoveryCodes)
            .SingleAsync();
        enrollment.Status.ShouldBe(PlatformMfaEnrollmentStatus.Active);
        enrollment.RecoveryCodes.ShouldAllBe(code => code.IsAvailable, "a refused recovery spends nothing");
    }

    private static async Task<Guid> IdentityIdAsync(string normalizedEmail)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var upper = normalizedEmail.ToUpperInvariant();
        return (await context.Users.AsNoTracking().SingleAsync(user => user.NormalizedEmail == upper)).Id;
    }
}
