using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// Who may reach an MFA gate at all (IA-REQ-041). The requirement is four conditions together, and each test here
/// removes exactly one of them: the gate must refuse every time, and always with the same code, because telling a
/// caller which condition they failed would tell them about state they were never shown.
/// </summary>
public sealed class PlatformMfaAuthenticationTests : TestBase
{
    [Test]
    public async Task An_anonymous_caller_holding_the_token_cannot_enroll()
    {
        var (_, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();

        // The pipeline refuses it before the handler runs, which is the stronger guarantee: the gate is not
        // merely careful about anonymous callers, it is unreachable by them.
        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token)));

        (await TestApp.CountAsync<PlatformMfaEnrollment>()).ShouldBe(0);
    }

    /// <summary>
    /// The stolen-token case. A signed-in identity that is not the one the offer was bound to holds no claim on
    /// it, however valid the token they present.
    /// </summary>
    [Test]
    public async Task A_signed_in_identity_the_invitation_was_not_bound_to_cannot_enroll()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        var stranger = await IdentityHttpHarness.SeedConfirmedUserAsync($"stranger-{Guid.NewGuid():N}@example.test", PlatformScenario.ValidPassword);
        PlatformScenario.RunAs(stranger);

        var result = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await TestApp.CountAsync<PlatformMfaEnrollment>()).ShouldBe(0);
        email.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// An unconfirmed address is one nobody has proved they can read, so it cannot be the address the offer was
    /// made to. Confirmation stays an actual gate rather than a formality.
    /// </summary>
    [Test]
    public async Task An_identity_that_never_confirmed_its_address_cannot_enroll()
    {
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        PlatformScenario.RunAs(identity.Id);

        var result = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
        (await TestApp.CountAsync<PlatformMfaEnrollment>()).ShouldBe(0);
    }

    [Test]
    public async Task A_token_that_resolves_to_nothing_admits_nobody()
    {
        var identity = await IdentityHttpHarness.SeedConfirmedUserAsync($"nobody-{Guid.NewGuid():N}@example.test", PlatformScenario.ValidPassword);
        PlatformScenario.RunAs(identity);

        var result = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_invitation");
    }

    /// <summary>An expired offer is unusable, so it cannot let anybody past a gate either.</summary>
    [Test]
    public async Task An_expired_invitation_admits_nobody()
    {
        await PlatformScenario.SeedPlatformRolesAsync();
        var (email, token) = await PlatformScenario.PendingInvitationAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        var confirmationToken = await PlatformScenario.SealedTokenAsync((await PlatformScenario.MessagesAsync()).Single().Id);
        await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmationToken));
        await PlatformScenario.ExpireInvitationAsync();
        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        PlatformScenario.RunAs(identity.Id);

        var result = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));

        result.IsFailure.ShouldBeTrue();
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    /// <summary>
    /// The gates are declared to the HTTP surface as authenticated routes. An unauthenticated call must be refused
    /// by the pipeline itself, before any handler decides anything.
    /// </summary>
    [TestCase("/api/platform/mfa/enroll")]
    [TestCase("/api/platform/mfa/verify")]
    [TestCase("/api/platform/mfa/recovery-acknowledge")]
    [TestCase("/api/platform/mfa/step-up")]
    public async Task An_unauthenticated_request_to_a_gate_is_refused_by_the_pipeline(string route)
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        const string host = "https://platform.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(harness.Client, host);

        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}{route}", new { token = "x", code = "000000" }, antiforgery);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>Every state-changing Platform route requires antiforgery; none may be normalized to a business answer.</summary>
    [TestCase("/api/platform/invitations/register")]
    [TestCase("/api/platform/invitations/confirm")]
    public async Task A_state_changing_platform_route_without_antiforgery_is_a_typed_400(string route)
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        const string host = "https://platform.localhost";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}{route}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { token = "x", password = "Testing1234!", confirmationToken = "y" })
        };
        request.Headers.Add("Origin", host);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await IdentityHttpHarness.ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("antiforgery_validation_failed");
    }
}
