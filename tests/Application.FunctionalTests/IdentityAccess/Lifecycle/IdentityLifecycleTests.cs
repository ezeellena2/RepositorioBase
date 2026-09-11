using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Lifecycle;

/// <summary>
/// Parking your own account and coming back (IA-REQ-054, C6 with withdrawal E1).
/// <para>
/// Every question here is asked over the production pipeline, because what is being tested is what a person
/// holding a cookie, a password or a mailed ticket can actually reach. E1 is why no test here mentions a provider:
/// the way back is the ticket and a password, and a provider-only identity sets one first.
/// </para>
/// </summary>
public sealed class IdentityLifecycleTests : TestBase
{
    private const string Password = "Testing1234!";
    private const string OtherPassword = "Replaced5678!";

    private static string Host() => $"https://lifecycle-{Guid.NewGuid():N}.localhost";

    [TestCase("identity.lifecycle.self.deactivated.notice.requested", "/account/reactivation-request")]
    [TestCase("identity.lifecycle.administratively.suspended.notice.requested", "/login")]
    [TestCase("identity.lifecycle.reactivated.notice.requested", "/login")]
    public async Task Each_lifecycle_notice_type_delivers_its_fixed_template(string messageType, string path)
    {
        var email = $"notice-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = OutboxMessage.Create(
            messageType,
            JsonSerializer.Serialize(new { IdentityId = identityId }),
            DateTimeOffset.UtcNow);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var sink = await DispatchMailAsync(scope);
        var delivered = sink.Messages.SingleOrDefault(item => item.Key == message.Id.ToString());
        delivered.Recipient.ShouldBe(email);
        delivered.Body.ShouldEndWith(path);
        delivered.Body.ShouldNotContain("#token=");
        await context.Entry(message).ReloadAsync();
        message.Status.ShouldBe(OutboxMessageStatus.Delivered);
    }

    [TestCase("expired")]
    [TestCase("superseded")]
    [TestCase("consumed")]
    [TestCase("wrong-ticket")]
    [TestCase("suspended")]
    public async Task Delivery_does_not_mail_a_ticket_that_is_no_longer_usable(string reason)
    {
        var email = $"undeliverable-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);
        await RequestReturnAsync(client, host, email);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ticket = await context.AccountReactivationRequests.SingleAsync();
        var message = await context.OutboxMessages.SingleAsync(item => item.Type == "identity.reactivation.requested");
        var now = DateTimeOffset.UtcNow;
        switch (reason)
        {
            case "expired":
                context.Entry(ticket).Property(item => item.IssuedAt).CurrentValue = now.AddMinutes(-31);
                context.Entry(ticket).Property(item => item.ExpiresAt).CurrentValue = now.AddMinutes(-1);
                break;
            case "superseded": ticket.Supersede(now); break;
            case "consumed": ticket.Consume(now); break;
            case "wrong-ticket": context.Entry(ticket).Property(item => item.TokenHash).CurrentValue = VersionedTokenHash.Of("another-ticket"); break;
            case "suspended":
                var user = await context.Users.SingleAsync(user => user.Id == ticket.IdentityId);
                user.StatusBeforeSuspension = IdentityAccountStatus.SelfDeactivated;
                user.Status = IdentityAccountStatus.AdministrativelySuspended;
                break;
        }
        await context.SaveChangesAsync();
        var sink = await DispatchMailAsync(scope);
        sink.Messages.ShouldNotContain(item => item.Key == message.Id.ToString());
        await context.Entry(message).ReloadAsync();
        message.Status.ShouldBe(OutboxMessageStatus.Abandoned);
    }

    [Test]
    public async Task Parking_an_account_shuts_every_door_it_had_open()
    {
        var email = $"park-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();

        var phone = await SignInAsync(client, host, email, Password);
        var laptop = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, laptop, ProofActions.AccountDeactivate);

        using var parked = await DeactivateAsync(client, host, laptop);
        parked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await parked.Content.ReadAsStringAsync());
        await AssertIdentityOnlyNoticeAsync("identity.lifecycle.self.deactivated.notice.requested", identityId);

        // The cookie that asked, and the one that was never told.
        (await ContextAsync(client, host, laptop)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ContextAsync(client, host, phone)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the password that opened both of them opens nothing now.
        (await TrySignInAsync(client, host, email, Password)).ShouldBeNull("a parked account must not answer its own password with a session");
    }

    [Test]
    public async Task A_parked_account_comes_back_with_the_mailed_ticket_and_its_own_password()
    {
        var email = $"return-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);

        (await RequestReturnAsync(client, host, email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var ticket = await DeliveredTicketAsync();

        using var returned = await ReactivateAsync(client, host, ticket, Password);
        returned.StatusCode.ShouldBe(HttpStatusCode.NoContent, await returned.Content.ReadAsStringAsync());

        // Holding a mailed ticket is not the same as having signed in, so coming back hands out nothing.
        returned.Headers.Contains("Set-Cookie").ShouldBeFalse("reactivation issues no session and no cookie");

        (await TrySignInAsync(client, host, email, Password)).ShouldNotBeNull("a reactivated account signs in again with its own password");
    }

    [Test]
    public async Task A_ticket_alone_is_not_a_way_back()
    {
        var email = $"ticket-only-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);
        await RequestReturnAsync(client, host, email);
        var ticket = await DeliveredTicketAsync();

        using var refused = await ReactivateAsync(client, host, ticket, OtherPassword);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("invalid_reactivation");
        (await TrySignInAsync(client, host, email, Password)).ShouldBeNull("a refused answer leaves the account parked");

        // One wrong answer does not burn the ticket; the person who holds the mailbox still holds the way back.
        using var accepted = await ReactivateAsync(client, host, ticket, Password);
        accepted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task A_spent_ticket_is_worth_exactly_what_a_forged_one_is()
    {
        var email = $"spent-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);
        await RequestReturnAsync(client, host, email);
        var ticket = await DeliveredTicketAsync();
        (await ReactivateAsync(client, host, ticket, Password)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var reused = await ReactivateAsync(client, host, ticket, Password);
        using var forged = await ReactivateAsync(client, host, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), Password);
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Everything the two answers say, except the per-request trace identifier, which every response carries
        // and which says nothing about the ticket.
        (await DescribedAsync(reused)).ShouldBe(await DescribedAsync(forged),
            "a spent ticket and a forged one must be indistinguishable");
    }

    [Test]
    public async Task Reissuing_the_way_back_kills_the_one_before_it()
    {
        var email = $"reissue-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);

        await RequestReturnAsync(client, host, email);
        var first = await DeliveredTicketAsync();
        await RequestReturnAsync(client, host, email);
        var second = await DeliveredTicketAsync();
        second.ShouldNotBe(first);

        (await ReactivateAsync(client, host, first, Password)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReactivateAsync(client, host, second, Password)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Asking_to_come_back_says_the_same_thing_about_every_address()
    {
        var parkedEmail = $"neutral-parked-{Guid.NewGuid():N}@example.test";
        var liveEmail = $"neutral-live-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(parkedEmail, Password);
        await IdentityHttpHarness.SeedConfirmedUserAsync(liveEmail, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, parkedEmail);

        using var unknown = await RequestReturnAsync(client, host, $"nobody-{Guid.NewGuid():N}@example.test");
        using var live = await RequestReturnAsync(client, host, liveEmail);
        using var parked = await RequestReturnAsync(client, host, parkedEmail);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        live.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        parked.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Identical answers, and only one of the three wrote anything for a dispatcher to deliver.
        (await ReturnMessagesAsync()).Count.ShouldBe(1, "only a parked account has anything to be sent");
    }

    [Test]
    public async Task Replacing_a_forgotten_password_is_not_a_way_out_of_being_parked()
    {
        var email = $"reset-not-lifecycle-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        await ParkAsync(client, host, email);

        var antiforgery = await AntiforgeryAsync(client, host, null);
        using (var recovery = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/password/recovery", new { email }, antiforgery.Token))
        {
            recovery.Headers.Add("Cookie", antiforgery.Cookie);
            (await client.SendAsync(recovery)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var resetToken = await DeliveredPasswordTokenAsync();
        var reset = await AntiforgeryAsync(client, host, null);
        using (var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/credentials/password/reset", new { token = resetToken, newPassword = OtherPassword }, reset.Token))
        {
            request.Headers.Add("Cookie", reset.Cookie);
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // The credential changed. What the account is allowed to do did not.
        (await TrySignInAsync(client, host, email, OtherPassword)).ShouldBeNull(
            "recovering a credential changes what a person can prove, never what their account may do");
    }

    [Test]
    public async Task Parking_your_account_cannot_leave_an_organization_with_nobody_to_run_it()
    {
        var email = $"last-admin-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        await IdentityHttpHarness.SeedPermissionCatalogAsync();
        await IdentityHttpHarness.SeedActiveMembershipAsync(identityId, Permissions.RolesManage, Permissions.MembersManage);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, cookie, ProofActions.AccountDeactivate);

        using var refused = await DeactivateAsync(client, host, cookie);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("last_administrator_required");
        (await ContextAsync(client, host, cookie)).StatusCode.ShouldBe(HttpStatusCode.OK, "a refused parking leaves the account exactly as it was");
    }

    [Test]
    public async Task Parking_your_account_cannot_leave_the_Platform_with_no_owner()
    {
        var email = $"last-owner-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        await SeedPlatformOwnerAsync(identityId);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, cookie, ProofActions.AccountDeactivate);

        using var refused = await DeactivateAsync(client, host, cookie);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("platform_last_owner");
    }

    [Test]
    public async Task Parking_an_account_needs_the_password_proved_a_moment_ago()
    {
        var email = $"unproved-{Guid.NewGuid():N}@example.test";
        await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var client = WithoutCookieJar(harness);
        var host = Host();
        var cookie = await SignInAsync(client, host, email, Password);

        using var refused = await DeactivateAsync(client, host, cookie);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await IdentityHttpHarness.ReadProblemAsync(refused)).GetProperty("code").GetString().ShouldBe("recent_proof_required");
        (await ContextAsync(client, host, cookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ParkAsync(HttpClient client, string host, string email)
    {
        var cookie = await SignInAsync(client, host, email, Password);
        await ProveAsync(client, host, cookie, ProofActions.AccountDeactivate);
        using var parked = await DeactivateAsync(client, host, cookie);
        parked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await parked.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, string host, string cookie)
    {
        var antiforgery = await AntiforgeryAsync(client, host, cookie);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/account/deactivate", new { }, antiforgery.Token);
        request.Headers.Add("Cookie", $"{cookie}; {antiforgery.Cookie}");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RequestReturnAsync(HttpClient client, string host, string email)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/account/reactivation-requests", new { email }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ReactivateAsync(HttpClient client, string host, string reactivationToken, string password)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/account/reactivate", new { reactivationToken, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        return await client.SendAsync(request);
    }

    private static HttpClient WithoutCookieJar(IdentityHttpHarness.ProductionHarness harness) =>
        harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

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
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<string> SignInAsync(HttpClient client, string host, string email, string password) =>
        await TrySignInAsync(client, host, email, password)
        ?? throw new InvalidOperationException("The sign-in this test needs as its premise was refused.");

    private static async Task<string?> TrySignInAsync(HttpClient client, string host, string email, string password)
    {
        var antiforgery = await AntiforgeryAsync(client, host, null);
        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/identity/sessions", new { email, password }, antiforgery.Token);
        request.Headers.Add("Cookie", antiforgery.Cookie);
        using var response = await client.SendAsync(request);
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        return cookies.FirstOrDefault(value => value.StartsWith("__Host-ia-auth=", StringComparison.Ordinal))?.Split(';')[0];
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

    private static async Task AssertIdentityOnlyNoticeAsync(string messageType, Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages.AsNoTracking()
            .Where(candidate => candidate.Type == messageType)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstAsync();
        using var payload = JsonDocument.Parse(message.Payload);
        var properties = payload.RootElement.EnumerateObject().ToArray();
        properties.Select(property => property.Name).ShouldBe(["IdentityId"]);
        properties[0].Value.GetGuid().ShouldBe(identityId);
    }

    private static async Task<List<OutboxMessage>> ReturnMessagesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboxMessages
            .Where(message => message.Type == "identity.reactivation.requested")
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Follow the mail produced by the registered handler and the real dispatcher.</summary>
    private static async Task<string> DeliveredTicketAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages.Where(item => item.Type == "identity.reactivation.requested")
            .OrderByDescending(item => item.CreatedAt).FirstAsync();
        var sink = await DispatchMailAsync(scope);
        var delivered = sink.Messages.Where(item => item.Key == message.Id.ToString()).ToList();
        delivered.Count.ShouldBe(1, "the reactivation link must be delivered exactly once by a registered handler");
        var recipient = await context.Users.Where(user => user.Id == context.AccountReactivationRequests
            .OrderByDescending(ticket => ticket.IssuedAt).Select(ticket => ticket.IdentityId).First()).Select(user => user.Email).SingleAsync();
        delivered[0].Recipient.ShouldBe(recipient);
        var link = new Uri(delivered[0].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last());
        link.AbsolutePath.ShouldBe("/account/reactivate");
        link.Query.ShouldBeEmpty();
        return Uri.UnescapeDataString(link.Fragment["#token=".Length..]);
    }

    private static async Task<LifecycleDeliverySink> DispatchMailAsync(IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sink = new LifecycleDeliverySink();
        var dispatcher = new OutboxDispatcher(context, scope.ServiceProvider.GetRequiredService<IOutboxSecretReader>(),
            scope.ServiceProvider.GetServices<IOutboxDeliveryHandler>(), TimeProvider.System, sink,
            scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Lifecycle.IRecoveryAdmission>(), FunctionalTestMetrics.Instance);
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        await dispatcher.DispatchDueAsync(CancellationToken.None);
        return sink;
    }

    private sealed class LifecycleDeliverySink : IIdentityEmailSender
    {
        public List<(string Recipient, string Body, string Key)> Messages { get; } = [];
        public Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
        {
            Messages.Add((recipient, body, idempotencyKey));
            return Task.FromResult(new EmailDeliveryReceipt(true, Guid.NewGuid().ToString(), false));
        }
    }

    private static async Task<string> DeliveredPasswordTokenAsync() =>
        await SealedTokenAsync("identity.password.recovery.requested");

    private static async Task<string> SealedTokenAsync(string messageType)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages
            .Where(candidate => candidate.Type == messageType)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstAsync();
        var reader = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader>();
        return (await reader.ReadAsync(message.Id, CancellationToken.None))!;
    }

    /// <summary>The only active owner of the Platform, seeded as the premise this test needs.</summary>
    private static async Task SeedPlatformOwnerAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platform = Tenant.CreatePlatform();
        platform.Activate();
        var owner = Role.CreateSystem(platform, "Owner");
        var membership = TenantMembership.CreateResponsible(platform, identityId);
        membership.Activate(platform);
        context.AddRange(platform, owner, membership);
        context.Add(MembershipRole.Create(platform, membership, owner));
        await context.SaveChangesAsync();
    }

    /// <summary>Everything a problem response claims, minus the trace identifier that is new every time.</summary>
    private static async Task<string> DescribedAsync(HttpResponseMessage response)
    {
        var problem = await IdentityHttpHarness.ReadProblemAsync(response);
        var described = problem.EnumerateObject()
            .Where(member => member.Name != "traceId")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}");
        return $"{(int)response.StatusCode} {string.Join('|', described)}";
    }

    private sealed record Antiforgery(string Cookie, string Token);
}
