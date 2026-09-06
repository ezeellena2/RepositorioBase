using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.ExternalLogins;

/// <summary>
/// Signing in with a provider, and linking or unlinking one (IA-REQ-052, BR-ID-005/006).
/// <para>
/// Every case here drives the framework's own OpenID Connect handler against <see cref="ControlledOidcProvider"/>:
/// a real challenge, a real authorization code, a real server-side exchange with the PKCE verifier, and a real
/// signature, issuer, audience, expiry and nonce validation. Nothing stubs the resulting principal, because the
/// controls being claimed are exactly the ones a stub would skip.
/// </para>
/// </summary>
public sealed class GoogleOidcTests : TestBase
{
    private const string Password = "Testing1234!";

    [Test]
    public async Task A_verified_provider_account_nobody_has_claimed_becomes_an_identity_with_a_session_and_no_tenant()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();

        var completed = await browser.SignInWithProviderAsync("google-subject-1", "newcomer@provider.test");

        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());
        var identity = (await TestApp.ListAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()).Single();
        identity.Email.ShouldBe("newcomer@provider.test");
        identity.EmailConfirmed.ShouldBeTrue("the provider asserted the address, which is the only reason it counts as confirmed");
        identity.PasswordHash.ShouldBeNull("nobody chose a password, so none was invented");
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0, "arriving through a provider joins nothing; onboarding stays an explicit choice");
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(1);
    }

    [Test]
    public async Task A_provider_address_that_already_has_a_local_account_is_refused_rather_than_adopted()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();

        var completed = await browser.SignInWithProviderAsync("google-subject-2", email);

        completed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(completed)).GetProperty("code").GetString().ShouldBe("external_login_conflict");
        (await LinksOfAsync(identityId)).ShouldBeEmpty("a matching address is not a proof of ownership (BR-ID-005/006)");
        (await TestApp.CountAsync<UserSession>()).ShouldBe(0, "nothing signed in");
    }

    [Test]
    public async Task An_address_the_provider_did_not_verify_signs_nobody_in()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();

        var completed = await browser.SignInWithProviderAsync("google-subject-3", "unverified@provider.test", emailVerified: false);

        completed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(completed)).GetProperty("code").GetString().ShouldBe("invalid_external_login");
        (await TestApp.CountAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()).ShouldBe(0);
    }

    [Test]
    public async Task Coming_back_with_the_same_provider_account_signs_the_same_identity_in_again()
    {
        using var scenario = Scenario();

        (await scenario.Browser().SignInWithProviderAsync("google-subject-4", "returning@provider.test")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await scenario.Browser().SignInWithProviderAsync("google-subject-4", "returning@provider.test")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TestApp.CountAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>())
            .ShouldBe(1, "the second visit recognized the subject rather than creating a second identity");
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(2);
    }

    [Test]
    public async Task The_code_is_exchanged_by_the_server_with_the_verifier_the_challenge_committed_to()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();

        var challenge = await browser.StartAsync("login");
        challenge.Parameters["response_type"].ToString().ShouldBe("code");
        challenge.Parameters["code_challenge_method"].ToString().ShouldBe("S256", "PKCE, and the strong method");
        challenge.Parameters["code_challenge"].ToString().ShouldNotBeNullOrWhiteSpace();
        challenge.Parameters["nonce"].ToString().ShouldNotBeNullOrWhiteSpace();
        challenge.Parameters["response_mode"].ToString().ShouldBe("form_post",
            "the authorization code comes back in a body, so it never reaches a URL that gets logged or referred");
        challenge.Parameters["redirect_uri"].ToString().ShouldBe($"{browser.Host}/api/identity/external/google/callback");
        challenge.Parameters.ContainsKey("client_secret").ShouldBeFalse("the secret is only ever sent on the back channel");

        var callback = await browser.CallbackAsync(challenge, "google-subject-5", "verifier@provider.test", true);
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The provider refuses an exchange whose verifier does not hash to the committed challenge, so a single
        // successful exchange is the proof that the handler really sent the right one.
        scenario.Provider.TokenExchanges.ShouldBe(1);
        callback.Headers.Location!.ToString().ShouldBe("/external/return?outcome=signed_in");
    }

    [Test]
    public async Task A_tampered_state_never_becomes_a_validated_handoff()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();
        var challenge = await browser.StartAsync("login");

        var callback = await browser.CallbackAsync(challenge, "google-subject-6", "tamper@provider.test", true, stateSuffix: "x");

        callback.Headers.Location!.ToString().ShouldBe("/external/return?outcome=refused");
        scenario.Provider.TokenExchanges.ShouldBe(0, "a state that does not unprotect is refused before any code is spent");
        (await HandoffAsync()).Status.ShouldBe(ExternalAuthorizationStatus.Started, "nothing was recorded against it");
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("issuer")]
    [TestCase("audience")]
    [TestCase("signature")]
    [TestCase("expiry")]
    [TestCase("nonce")]
    public async Task An_assertion_the_protocol_refuses_reaches_no_application_decision(string flaw)
    {
        using var scenario = Scenario();
        switch (flaw)
        {
            case "issuer": scenario.Provider.IssuerClaim = "https://impostor.test"; break;
            case "audience": scenario.Provider.AudienceClaim = "some-other-client"; break;
            case "signature": scenario.Provider.SignWithUnpublishedKey = true; break;
            case "expiry": scenario.Provider.IdTokenLifetime = TimeSpan.FromMinutes(-30); break;
            default: scenario.Provider.NonceOverride = $"replayed-{Guid.NewGuid():N}"; break;
        }

        var browser = scenario.Browser();
        var challenge = await browser.StartAsync("login");
        var callback = await browser.CallbackAsync(challenge, "google-subject-7", "refused@provider.test", true);

        callback.Headers.Location!.ToString().ShouldBe("/external/return?outcome=refused");
        (await HandoffAsync()).Status.ShouldBe(ExternalAuthorizationStatus.Failed, "the round trip is settled, not left open for a retry");
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TestApp.CountAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()).ShouldBe(0);
    }

    [Test]
    public async Task A_replayed_callback_cannot_produce_a_second_session()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();
        var challenge = await browser.StartAsync("login");
        await browser.CallbackAsync(challenge, "google-subject-8", "replay@provider.test", true);
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replayed = await browser.CallbackAsync(challenge, "google-subject-8", "replay@provider.test", true);

        replayed.Headers.Location!.ToString().ShouldBe("/external/return?outcome=refused",
            "the correlation is one-use, so the same state and code cannot be presented twice");
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TestApp.ListAsync<UserSession>()).Count(session => session.RevokedAt is null).ShouldBe(1);
    }

    [Test]
    public async Task A_round_trip_started_to_sign_in_links_nothing_to_the_session_that_happened_to_be_open()
    {
        var email = $"mixer-{Guid.NewGuid():N}@example.test";
        var localId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(email);

        var challenge = await browser.StartAsync("login");
        await browser.CallbackAsync(challenge, "google-subject-9", "crossed@provider.test", true);
        var completed = await browser.CompleteAsync();

        // There is one completion route and it takes no purpose: which effect it may have is read from the
        // cookie the callback sealed, so a caller cannot ask a sign-in to link instead. What a sign-in does while
        // somebody else's session happens to be open is sign that other person in — never quietly attach a
        // provider account to the session that was already there.
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());
        (await LinksOfAsync(localId)).ShouldBeEmpty("the signed-in identity gained nothing it did not ask for");
        var arrived = (await TestApp.ListAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>())
            .Single(user => user.Email == "crossed@provider.test");
        (await LinksOfAsync(arrived.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Linking_needs_consent_and_a_recent_proof_before_the_provider_is_ever_reached()
    {
        var email = $"careful-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(email);

        var withoutConsent = await browser.PostAsync("/api/identity/external/Google/link/start", new { consent = false });
        var withoutProof = await browser.PostAsync("/api/identity/external/Google/link/start", new { consent = true });

        withoutConsent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(withoutConsent)).GetProperty("code").GetString().ShouldBe("invalid_external_login");
        withoutProof.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(withoutProof)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await TestApp.CountAsync<ExternalAuthorizationRequest>()).ShouldBe(0, "nothing was started, so nothing reached the provider");
    }

    [Test]
    public async Task A_person_links_their_own_provider_account_and_sees_it_listed()
    {
        var email = $"linker-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(email);

        var linked = await browser.LinkAsync("google-subject-10", email);

        linked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await linked.Content.ReadAsStringAsync());
        var listed = (await browser.GetAsync("/api/identity/external"));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = (await listed.Content.ReadFromJsonAsync<LinkList>())!.Items;
        rows.Single().Provider.ShouldBe("Google");
        rows.Single().ProviderEmail.ShouldBe(email);
        rows.Single().Handle.ShouldNotBeNullOrWhiteSpace();
        rows.Single().LinkedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
        (await ReadBodyAsync(listed)).Contains("google-subject-10", StringComparison.Ordinal)
            .ShouldBeFalse("the provider's identifier for the person stays on the server");
        (await LinksOfAsync(identityId)).Count.ShouldBe(1);
        (await SecurityVersionAsync(identityId)).ShouldBeGreaterThan(0, "adding a way in is a credential change, so outstanding proofs die with it");
    }

    [Test]
    public async Task A_provider_account_somebody_else_already_linked_cannot_be_taken()
    {
        var first = $"first-{Guid.NewGuid():N}@example.test";
        var second = $"second-{Guid.NewGuid():N}@example.test";
        var firstId = await IdentityHttpHarness.SeedConfirmedUserAsync(first, Password);
        var secondId = await IdentityHttpHarness.SeedConfirmedUserAsync(second, Password);
        using var scenario = Scenario();

        var owner = scenario.Browser();
        await owner.SignInWithPasswordAsync(first);
        (await owner.LinkAsync("google-subject-11", first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var claimant = scenario.Browser();
        await claimant.SignInWithPasswordAsync(second);
        var stolen = await claimant.LinkAsync("google-subject-11", second);

        stolen.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(stolen)).GetProperty("code").GetString().ShouldBe("external_login_conflict");
        (await LinksOfAsync(firstId)).Count.ShouldBe(1);
        (await LinksOfAsync(secondId)).ShouldBeEmpty();
    }

    [Test]
    public async Task A_provider_address_belonging_to_another_local_account_cannot_be_linked()
    {
        var mine = $"mine-{Guid.NewGuid():N}@example.test";
        var theirs = $"theirs-{Guid.NewGuid():N}@example.test";
        var myId = await IdentityHttpHarness.SeedConfirmedUserAsync(mine, Password);
        await IdentityHttpHarness.SeedConfirmedUserAsync(theirs, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(mine);

        var refused = await browser.LinkAsync("google-subject-12", theirs);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LinksOfAsync(myId)).ShouldBeEmpty("linking must not carry another person's address into this account (amendment A2)");
    }

    [Test]
    public async Task Linking_a_second_account_for_a_provider_that_is_already_linked_is_refused()
    {
        var email = $"twice-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(email);
        (await browser.LinkAsync("google-subject-13", email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await browser.LinkAsync("google-subject-14", email, expectStart: HttpStatusCode.Conflict);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(again)).GetProperty("code").GetString().ShouldBe("provider_already_linked");
        (await LinksOfAsync(identityId)).Single().ProviderKey.ShouldBe("google-subject-13");
    }

    [Test]
    public async Task Unlinking_spends_a_proof_and_leaves_the_password_working()
    {
        var email = $"unlinker-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scenario = Scenario();
        var browser = scenario.Browser();
        await browser.SignInWithPasswordAsync(email);
        (await browser.LinkAsync("google-subject-15", email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await browser.SignInWithPasswordAsync(email);

        var unproved = await browser.DeleteAsync("/api/identity/external/Google");
        await browser.ProveAsync(ProofActions.ExternalUnlink);
        var unlinked = await browser.DeleteAsync("/api/identity/external/Google");

        unproved.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(unproved)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        unlinked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await unlinked.Content.ReadAsStringAsync());
        (await LinksOfAsync(identityId)).ShouldBeEmpty();
        (await IdentityHttpHarness.GetUserAsync(identityId)).PasswordHash.ShouldNotBeNull("the other way in is untouched");
    }

    [Test]
    public async Task An_identity_that_arrived_through_the_provider_is_told_it_has_no_password()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();
        (await browser.SignInWithProviderAsync("google-subject-19", "no-password@provider.test")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var credentials = await browser.GetAsync("/api/identity/credentials");

        credentials.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await credentials.Content.ReadFromJsonAsync<CredentialsRow>();

        // This is what lets the account screen explain the unlink it is about to refuse, instead of offering a
        // button whose only possible answer is `last_authenticator_required`.
        body!.HasPassword.ShouldBeFalse();
        body.PasswordUpdatedAt.ShouldBeNull();
    }

    [Test]
    public async Task The_last_way_into_an_account_cannot_be_removed()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();
        (await browser.SignInWithProviderAsync("google-subject-16", "only-way@provider.test")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var identityId = (await TestApp.ListAsync<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()).Single().Id;

        // The proof itself comes from the provider, because this identity has no password to prove with — which
        // is the whole situation the rule exists for.
        await browser.ProveThroughProviderAsync("google-subject-16", "only-way@provider.test", ProofActions.ExternalUnlink);
        var refused = await browser.DeleteAsync("/api/identity/external/Google");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("last_authenticator_required");
        (await LinksOfAsync(identityId)).Count.ShouldBe(1, "removing it would have left nobody able to sign in at all");
    }

    [Test]
    public async Task Two_identities_racing_for_one_provider_account_leave_exactly_one_link()
    {
        var first = $"race-a-{Guid.NewGuid():N}@example.test";
        var second = $"race-b-{Guid.NewGuid():N}@example.test";
        var firstId = await IdentityHttpHarness.SeedConfirmedUserAsync(first, Password);
        var secondId = await IdentityHttpHarness.SeedConfirmedUserAsync(second, Password);
        using var scenario = Scenario();

        var one = scenario.Browser();
        var two = scenario.Browser();
        await one.SignInWithPasswordAsync(first);
        await two.SignInWithPasswordAsync(second);
        await one.ArmLinkAsync("google-subject-17", first);
        await two.ArmLinkAsync("google-subject-17", second);

        var answers = await Task.WhenAll(one.CompleteAsync(), two.CompleteAsync());

        answers.Count(answer => answer.StatusCode == HttpStatusCode.NoContent)
            .ShouldBe(1, "the provider account belongs to one identity, and the store's own key is what decides which");
        ((await LinksOfAsync(firstId)).Count + (await LinksOfAsync(secondId)).Count).ShouldBe(1);
    }

    [Test]
    public async Task A_provider_this_deployment_does_not_offer_is_refused_before_anything_is_started()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();

        var refused = await browser.PostAsync("/api/identity/external/Facebook/login/start", null);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_external_login");
        (await TestApp.CountAsync<ExternalAuthorizationRequest>()).ShouldBe(0);
    }

    [Test]
    public async Task Nothing_the_callback_carried_is_written_to_a_log_or_returned_to_the_browser()
    {
        using var scenario = Scenario();
        var browser = scenario.Browser();
        var challenge = await browser.StartAsync("login");
        var callback = await browser.CallbackAsync(challenge, "google-subject-18", "quiet@provider.test", true);
        (await browser.CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var code = browser.LastCode!;
        var carrying = TestApp.CapturedLogs
            .Where(entry => entry.Contains(code, StringComparison.Ordinal) || entry.Contains(ControlledOidcProvider.ClientSecret, StringComparison.Ordinal))
            .ToArray();

        // Nothing this feature writes carries it, at any level.
        carrying.Where(entry => entry.Contains("] CleanArchitecture.", StringComparison.Ordinal))
            .ShouldBeEmpty($"the code and the secret reach the server and stop there: {string.Join(" | ", carrying)}");

        // And nothing a deployment writes carries it either. The one place that formats the code into a message
        // is the handler's own protocol tracing, which is Debug and is pinned above Debug by configuration; this
        // suite captures everything from Trace up, which is why the level has to be asserted rather than assumed.
        carrying.Where(entry => !entry.StartsWith("[Debug]", StringComparison.Ordinal) && !entry.StartsWith("[Trace]", StringComparison.Ordinal))
            .ShouldBeEmpty($"nothing at Information or above may carry it: {string.Join(" | ", carrying)}");
        ((int)scenario.OidcLogFloor()).ShouldBeGreaterThanOrEqualTo((int)LogLevel.Information,
            "a deployment cannot turn that tracing back on by asking for Debug");
        callback.Headers.Location!.ToString().ShouldBe("/external/return?outcome=signed_in", "a fixed local path carrying one word from a closed set, never anything the callback said");
        (await TestApp.ListAsync<ExternalAuthorizationRequest>()).Single().Status.ShouldBe(ExternalAuthorizationStatus.Consumed);
    }

    private static OidcScenario Scenario() => new();

    private static async Task<ExternalAuthorizationRequest> HandoffAsync() =>
        (await TestApp.ListAsync<ExternalAuthorizationRequest>()).OrderByDescending(request => request.CreatedAt).First();

    private static async Task<List<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>> LinksOfAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .UserLogins.AsNoTracking().Where(login => login.UserId == identityId).ToListAsync();
    }

    private static async Task<long> SecurityVersionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var state = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .IdentitySecurityStates.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId);
        return state?.SecurityVersion ?? 0;
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response) => response.Content.ReadAsStringAsync();

    private sealed record LinkRow(string Handle, string Provider, string ProviderEmail, DateTimeOffset LinkedAt);

    private sealed record LinkList(LinkRow[] Items);

    private sealed record CredentialsRow(bool HasPassword, DateTimeOffset? PasswordUpdatedAt);

    /// <summary>
    /// One host running with the controlled provider registered as Google. The client is configured from settings
    /// the host reads at build time, which is what makes the middleware exist at all.
    /// </summary>
    private sealed class OidcScenario : IDisposable
    {
        private readonly IdentityHttpHarness.ProductionHarness _harness;

        internal ControlledOidcProvider Provider { get; } = new();

        /// <summary>The level below which the handler's protocol tracing — the one place a code is formatted
        /// into a message — cannot be emitted, as this host is actually configured.</summary>
        internal LogLevel OidcLogFloor()
        {
            var rules = _harness.Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LoggerFilterOptions>>().Value.Rules;
            var rule = rules.LastOrDefault(candidate =>
                candidate.ProviderName is null &&
                candidate.CategoryName == "Microsoft.AspNetCore.Authentication.OpenIdConnect");
            return rule?.LogLevel ?? LogLevel.Trace;
        }

        internal OidcScenario()
        {
            var provider = Provider;
            _harness = IdentityHttpHarness.CreateProductionHarness(
                settings: new Dictionary<string, string?>
                {
                    ["IdentityAccess:ExternalLogins:Google:ClientId"] = ControlledOidcProvider.ClientId,
                    ["IdentityAccess:ExternalLogins:Google:ClientSecret"] = ControlledOidcProvider.ClientSecret,
                    ["IdentityAccess:ExternalLogins:Google:Authority"] = ControlledOidcProvider.Issuer
                },
                configureTestServices: services => services.Configure<OpenIdConnectOptions>(
                    ExternalProviders.Google,
                    options => options.BackchannelHttpHandler = provider.CreateHandler()));
        }

        internal ProviderBrowser Browser()
        {
            var host = $"https://oidc-{Guid.NewGuid():N}.localhost";
            return new ProviderBrowser(_harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
                BaseAddress = new Uri(host)
            }), host, Provider);
        }

        public void Dispose()
        {
            _harness.Dispose();
            Provider.Dispose();
        }
    }

    /// <summary>One browser: its own cookie jar, its own antiforgery pair, its own session.</summary>
    private sealed class ProviderBrowser(HttpClient client, string host, ControlledOidcProvider provider)
    {
        internal string Host { get; } = host;

        internal string? LastCode { get; private set; }

        internal async Task<HttpResponseMessage> SignInWithProviderAsync(string subject, string email, bool emailVerified = true)
        {
            var challenge = await StartAsync("login");
            await CallbackAsync(challenge, subject, email, emailVerified);
            return await CompleteAsync();
        }

        internal async Task<HttpResponseMessage> LinkAsync(string subject, string email, HttpStatusCode expectStart = HttpStatusCode.OK)
        {
            var started = await ArmLinkAsync(subject, email, expectStart);
            return started ?? await CompleteAsync();
        }

        /// <summary>
        /// Everything up to the completion: the proof, the consent, the challenge and the callback. Split out so a
        /// race can arm two browsers and then let both finish at once.
        /// </summary>
        internal async Task<HttpResponseMessage?> ArmLinkAsync(string subject, string email, HttpStatusCode expectStart = HttpStatusCode.OK)
        {
            await ProveAsync(ProofActions.ExternalLink);
            var start = await PostAsync("/api/identity/external/Google/link/start", new { consent = true });
            if (start.StatusCode != expectStart) start.StatusCode.ShouldBe(expectStart, await start.Content.ReadAsStringAsync());
            if (start.StatusCode != HttpStatusCode.OK) return start;
            var challenge = await FollowAsync(start);
            await CallbackAsync(challenge, subject, email, true);
            return null;
        }

        internal async Task ProveThroughProviderAsync(string subject, string email, string action)
        {
            var start = await PostAsync("/api/identity/external/Google/proof/start", new { action });
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            var challenge = await FollowAsync(start);
            await CallbackAsync(challenge, subject, email, true);
            (await CompleteAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        internal async Task<Challenge> StartAsync(string purpose)
        {
            var start = await PostAsync($"/api/identity/external/Google/{purpose}/start", null);
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            return await FollowAsync(start);
        }

        private async Task<Challenge> FollowAsync(HttpResponseMessage start)
        {
            var uri = (await start.Content.ReadFromJsonAsync<ChallengeBody>())!.AuthorizationRequestUri;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Host}{uri}");
            var redirect = await client.SendAsync(request);
            redirect.StatusCode.ShouldBe(HttpStatusCode.Found, "the challenge hands the browser to the provider");
            var location = redirect.Headers.Location!;
            return new Challenge(QueryHelpers.ParseQuery(location.Query));
        }

        internal async Task<HttpResponseMessage> CallbackAsync(Challenge challenge, string subject, string email, bool emailVerified, string stateSuffix = "")
        {
            LastCode = provider.IssueCode(
                challenge.Parameters["nonce"].ToString(),
                challenge.Parameters["code_challenge"].ToString(),
                challenge.Parameters["redirect_uri"].ToString(),
                subject,
                email,
                emailVerified);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Host}/api/identity/external/google/callback")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = LastCode,
                    ["state"] = challenge.Parameters["state"].ToString() + stateSuffix
                })
            };
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Found, await response.Content.ReadAsStringAsync());
            return response;
        }

        internal Task<HttpResponseMessage> CompleteAsync() =>
            PostAsync("/api/identity/external/complete", null);

        internal async Task SignInWithPasswordAsync(string email)
        {
            var response = await PostAsync("/api/identity/sessions", new { email, password = Password });
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        internal async Task ProveAsync(string action)
        {
            var response = await PostAsync("/api/identity/credentials/reauthenticate", new { action, password = Password });
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        internal async Task<HttpResponseMessage> PostAsync(string path, object? body)
        {
            using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{Host}{path}", body, await TokenAsync());
            return await client.SendAsync(request);
        }

        internal async Task<HttpResponseMessage> DeleteAsync(string path)
        {
            using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Delete, $"{Host}{path}", null, await TokenAsync());
            return await client.SendAsync(request);
        }

        internal async Task<HttpResponseMessage> GetAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Host}{path}");
            return await client.SendAsync(request);
        }

        private async Task<string> TokenAsync() => await IdentityHttpHarness.GetAntiforgeryAsync(client, Host);
    }

    private sealed record ChallengeBody(string AuthorizationRequestUri);

    private sealed record Challenge(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Parameters);
}
