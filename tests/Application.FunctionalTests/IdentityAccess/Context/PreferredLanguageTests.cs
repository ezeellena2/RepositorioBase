using System.Net;
using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Context;

public sealed class PreferredLanguageTests : TestBase
{
    [Test]
    public async Task A_language_choice_follows_the_account_across_live_and_later_sessions_without_rotating_security()
    {
        var clock = new ControlledTimeProvider(new DateTimeOffset(2030, 9, 11, 12, 0, 0, TimeSpan.Zero));
        using var harness = CreateProductionHarness(clock);
        var first = harness.Client;
        using var second = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        const string host = "https://preferred-language.localhost";
        var email = $"language-{Guid.NewGuid():N}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        await SignInAsync(first, host, email, "Testing1234!");
        await SignInAsync(second, host, email, "Testing1234!");
        var sessionsBefore = await SessionSnapshotsAsync(identityId);
        sessionsBefore.Length.ShouldBe(2);
        var securityBefore = await IdentitySecuritySnapshotsAsync(identityId);
        var userBefore = await GetUserAsync(identityId);
        var antiforgery = await GetAntiforgeryAsync(first, host);

        using var request = JsonRequest(
            HttpMethod.Put,
            $"{host}/api/identity/context/language",
            new { language = "es" },
            antiforgery);
        var response = await first.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var context = await ReadJsonAsync(response);
        context.GetProperty("preferredLanguage").GetString().ShouldBe("es");
        response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeFalse(
            "changing language must not rotate the authentication cookie or antiforgery pair");
        await AssertContextLanguageAsync(first, host, "es");
        await AssertContextLanguageAsync(second, host, "es");

        var userAfter = await GetUserAsync(identityId);
        userAfter.PreferredLanguage.ShouldBe("es");
        userAfter.PasswordHash.ShouldBe(userBefore.PasswordHash);
        userAfter.SecurityStamp.ShouldBe(userBefore.SecurityStamp);
        userAfter.ConcurrencyStamp.ShouldBe(userBefore.ConcurrencyStamp);
        userAfter.AccessFailedCount.ShouldBe(userBefore.AccessFailedCount);
        userAfter.LockoutEnd.ShouldBe(userBefore.LockoutEnd);
        userAfter.TwoFactorEnabled.ShouldBe(userBefore.TwoFactorEnabled);
        (await SessionSnapshotsAsync(identityId)).ShouldBe(sessionsBefore);
        (await IdentitySecuritySnapshotsAsync(identityId)).ShouldBe(securityBefore);

        using var later = harness.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        await SignInAsync(later, host, email, "Testing1234!");
        await AssertContextLanguageAsync(later, host, "es");
    }

    [TestCase("")]
    [TestCase("ES")]
    [TestCase("fr")]
    [TestCase("inProgress")]
    public async Task Own_account_language_update_rejects_every_noncanonical_unsupported_value(
        string language)
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        var host = $"https://invalid-language-{Guid.NewGuid():N}.localhost";
        var email = $"invalid-language-{Guid.NewGuid():N}@example.test";
        await SeedConfirmedUserAsync(email, "Testing1234!");
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var antiforgery = await GetAntiforgeryAsync(client, host);

        using var request = JsonRequest(
            HttpMethod.Put,
            $"{host}/api/identity/context/language",
            new { language },
            antiforgery);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Select(property => property.Name).ShouldBe(["language"]);
        var detail = errors.GetProperty("language").EnumerateArray().Single();
        detail.EnumerateObject().Select(property => property.Name).ShouldBe(["code", "params"]);
        detail.GetProperty("code").GetString().ShouldBe(language.Length == 0 ? "required" : "unsupported_value");
        detail.GetProperty("params").ValueKind.ShouldBe(JsonValueKind.Object);
        detail.GetProperty("params").EnumerateObject().ShouldBeEmpty();
        problem.GetRawText().ShouldNotContain("Choose a", Case.Insensitive);
        problem.GetRawText().ShouldNotContain("PropertyValue", Case.Insensitive);
        problem.GetRawText().ShouldNotContain("DNI", Case.Insensitive);
        problem.GetRawText().ShouldNotContain("PIN", Case.Insensitive);
        if (language.Length > 0) problem.GetRawText().ShouldNotContain($"\"{language}\"");
        (await GetUserAsync((await GetOnlySessionAsync()).IdentityId)).PreferredLanguage.ShouldBeNull();
    }

    [Test]
    public async Task A_failed_context_projection_cannot_persist_a_language_change()
    {
        using var harness = CreateProductionHarness(configureTestServices: services =>
        {
            services.RemoveAll<IRequestHandler<GetIdentityContextQuery, Result<IdentityContext>>>();
            services.AddScoped<IRequestHandler<GetIdentityContextQuery, Result<IdentityContext>>, FailingContextProjection>();
        });
        var client = harness.Client;
        const string host = "https://language-projection.localhost";
        var email = $"language-projection-{Guid.NewGuid():N}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var antiforgery = await GetAntiforgeryAsync(client, host);

        using var request = JsonRequest(
            HttpMethod.Put,
            $"{host}/api/identity/context/language",
            new { language = "es" },
            antiforgery);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("invalid_session");
        (await GetUserAsync(identityId)).PreferredLanguage.ShouldBeNull();
    }

    [Test]
    public async Task Own_account_language_update_requires_antiforgery()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://language-antiforgery.localhost";
        var email = $"language-antiforgery-{Guid.NewGuid():N}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{host}/api/identity/context/language")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { language = "es" })
        };
        request.Headers.Add("Origin", host);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("antiforgery_validation_failed");
        (await GetUserAsync(identityId)).PreferredLanguage.ShouldBeNull();
    }

    [Test]
    public async Task Own_account_language_update_rejects_a_cross_origin_request()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://language-origin.localhost";
        var email = $"language-origin-{Guid.NewGuid():N}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!");
        var authCookie = await SignInAsync(client, host, email, "Testing1234!");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var antiforgery = await GetAntiforgeryAsync(client, host);

        using var request = JsonRequest(
            HttpMethod.Put,
            $"{host}/api/identity/context/language",
            new { language = "es" },
            antiforgery);
        request.Headers.Remove("Origin");
        request.Headers.Add("Origin", "https://cross-origin.localhost");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("antiforgery_validation_failed");
        (await GetUserAsync(identityId)).PreferredLanguage.ShouldBeNull();
    }

    [Test]
    public async Task Unauthenticated_language_update_with_a_valid_same_origin_antiforgery_pair_is_refused_without_mutation()
    {
        using var harness = CreateProductionHarness();
        var client = harness.Client;
        const string host = "https://language-anonymous.localhost";
        var email = $"language-anonymous-{Guid.NewGuid():N}@example.test";
        var identityId = await SeedConfirmedUserAsync(email, "Testing1234!", "en");
        var before = await GetUserAsync(identityId);
        var antiforgery = await GetAntiforgeryAsync(client, host);

        using var request = JsonRequest(
            HttpMethod.Put,
            $"{host}/api/identity/context/language",
            new { language = "es" },
            antiforgery);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.Unauthorized);
        problem.GetProperty("code").GetString().ShouldBe("authentication_required");
        var after = await GetUserAsync(identityId);
        after.PreferredLanguage.ShouldBe("en");
        after.PasswordHash.ShouldBe(before.PasswordHash);
        after.SecurityStamp.ShouldBe(before.SecurityStamp);
        after.ConcurrencyStamp.ShouldBe(before.ConcurrencyStamp);
        (await TestApp.CountAsync<UserSession>()).ShouldBe(0);
    }

    [Test]
    public async Task Organization_registration_captures_the_request_language_and_initializes_only_the_new_account()
    {
        TestApp.SetRequestLanguage("es");
        var command = new RegisterOrganizationCommand(
            $"new-language-{Guid.NewGuid():N}@example.test",
            "Testing1234!",
            "Language Snapshot",
            "30-12345678-9");

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<PendingRegistrationIntent>()).Single().Language.ShouldBe("es");

        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();
        (await TestApp.ListAsync<ApplicationUser>()).Single().PreferredLanguage.ShouldBe("es");
    }

    [Test]
    public async Task A_flow_that_finds_an_existing_account_never_overwrites_its_preference()
    {
        var email = $"existing-language-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Users.Where(user => user.Id == identityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PreferredLanguage, "en"));
        }
        TestApp.SetUserId(null);
        TestApp.SetValidatedOptionalSession(null, null);
        TestApp.SetRequestLanguage("es");

        (await TestApp.SendAsync(new RegisterOrganizationCommand(
            email,
            "Testing1234!",
            "Existing Language",
            "30-12345678-9"))).IsSuccess.ShouldBeTrue();

        (await TestApp.FindAsync<ApplicationUser>(identityId))!.PreferredLanguage.ShouldBe("en");
    }

    [Test]
    public async Task Reissuing_an_invitation_preserves_its_original_request_language_snapshot()
    {
        var organization = await InvitationScenario.SeedOrganizationAsync(Permissions.MembersInvite);
        InvitationScenario.ActAs(organization);
        var command = new InviteMemberCommand(
            organization.TenantId,
            $"snapshot-{Guid.NewGuid():N}@example.test",
            [organization.SecondRoleId]);
        TestApp.SetRequestLanguage("es");

        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();
        (await InvitationScenario.SingleInvitationAsync()).Language.ShouldBe("es");

        TestApp.SetRequestLanguage("en");
        (await TestApp.SendAsync(command)).IsSuccess.ShouldBeTrue();

        var invitation = await InvitationScenario.SingleInvitationAsync();
        invitation.Language.ShouldBe("es");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    private static async Task AssertContextLanguageAsync(HttpClient client, string host, string expected)
    {
        using var response = await client.GetAsync($"{host}/api/identity/context");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("preferredLanguage").GetString().ShouldBe(expected);
    }

    private static async Task<SessionSnapshot[]> SessionSnapshotsAsync(Guid identityId) =>
        (await TestApp.ListAsync<UserSession>())
            .Where(session => session.IdentityId == identityId)
            .OrderBy(session => session.Id.Value)
            .Select(session => new SessionSnapshot(
                session.Id.Value,
                session.IdentityId,
                session.PublicRef.Value,
                session.DeviceLabel,
                session.CreatedAt,
                session.LastSeenAt,
                session.IdleExpiresAt,
                session.AbsoluteExpiresAt,
                session.RevokedAt,
                session.ActiveTenantId?.Value,
                session.Version))
            .ToArray();

    private static async Task<IdentitySecuritySnapshot[]> IdentitySecuritySnapshotsAsync(Guid identityId) =>
        (await TestApp.ListAsync<IdentitySecurityState>())
            .Where(state => state.IdentityId == identityId)
            .Select(state => new IdentitySecuritySnapshot(
                state.IdentityId,
                state.SecurityVersion,
                state.UpdatedAt,
                state.PasswordUpdatedAt,
                state.Version))
            .ToArray();

    private sealed record SessionSnapshot(
        Guid Id,
        Guid IdentityId,
        string PublicRef,
        string DeviceLabel,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset IdleExpiresAt,
        DateTimeOffset AbsoluteExpiresAt,
        DateTimeOffset? RevokedAt,
        Guid? ActiveTenantId,
        int Version);

    private sealed record IdentitySecuritySnapshot(
        Guid IdentityId,
        long SecurityVersion,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? PasswordUpdatedAt,
        int Version);

    private sealed class FailingContextProjection : IRequestHandler<GetIdentityContextQuery, Result<IdentityContext>>
    {
        public Task<Result<IdentityContext>> Handle(GetIdentityContextQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession()));
    }
}
