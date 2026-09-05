using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// Bootstrap recovery (IA-REQ-040). It exists for one situation — the owner's invitation cannot arrive and there
/// is nobody yet who could fix it — and every test here is about how narrowly it stays inside that.
/// <para>
/// The neutrality is the point: an anonymous caller learns nothing from the answer, whether a bootstrap is
/// pending, whether it lapsed, or who it is for. Only two answers differ, and both are decided from the request
/// rather than from state.
/// </para>
/// </summary>
public sealed class PlatformBootstrapRecoveryTests : TestBase
{
    private const string OwnerEmail = "platform-owner@example.test";

    private static Task<CleanArchitecture.Application.Common.Models.Result> RecoverAsync() =>
        TestApp.SendAsync(new RecoverPendingPlatformOwnerInvitationCommand());

    [Test]
    public async Task An_expired_invitation_is_reissued_with_a_new_token_to_the_same_recipient()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var original = await PlatformScenario.PendingOwnerTokenAsync();
        await PlatformScenario.ExpireInvitationAsync();
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue();

        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.NormalizedEmail.ShouldBe(OwnerEmail, "recovery never changes the recipient.");
        invitation.TokenHash.Matches(original).ShouldBeFalse("the prior token is invalidated.");
        var replacement = await PlatformScenario.PendingOwnerTokenAsync();
        invitation.TokenHash.Matches(replacement).ShouldBeTrue();
        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(1, "one pending owner, always.");
    }

    /// <summary>
    /// The superseded envelope is retired in the same transaction that rotates the token, so there is never an
    /// instant when two tokens for one invitation are both deliverable.
    /// </summary>
    [Test]
    public async Task The_superseded_envelope_is_retired_and_one_deliverable_token_remains()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        await PlatformScenario.ExpireInvitationAsync();
        PlatformScenario.RunAnonymously();

        await RecoverAsync();

        var secrets = await TestApp.ListAsync<OutboxSecret>();
        secrets.Count.ShouldBe(2, "the message stays as history; only one envelope is live.");
        secrets.Count(secret => secret.Status == OutboxSecretStatus.Pending).ShouldBe(1);
        secrets.Count(secret => secret.Status == OutboxSecretStatus.Expired).ShouldBe(1);
    }

    [Test]
    public async Task A_permanently_failed_delivery_is_recovered_before_the_window_closes()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var original = await PlatformScenario.PendingOwnerTokenAsync();
        await PlatformScenario.FailDeliveryAsync();
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue();

        (await PlatformScenario.SingleInvitationAsync()).TokenHash.Matches(original).ShouldBeFalse();
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "platform.bootstrap.recovered");
    }

    /// <summary>An invitation still on its way must not be rotated, and the caller cannot tell that it was not.</summary>
    [Test]
    public async Task An_invitation_in_flight_answers_the_same_way_and_is_left_alone()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var original = await PlatformScenario.PendingOwnerTokenAsync();
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue("the answer is the same as a real recovery.");

        (await PlatformScenario.SingleInvitationAsync()).TokenHash.Matches(original).ShouldBeTrue();
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1, "nothing was written.");
    }

    [Test]
    public async Task With_no_bootstrap_at_all_the_answer_is_the_same_and_nothing_is_created()
    {
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue();

        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0, "recovery works before any identity exists, and creates none.");
    }

    /// <summary>Editing the configured address must not move the pending owner to it.</summary>
    [Test]
    public async Task A_configuration_change_recovers_nothing()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var original = await PlatformScenario.PendingOwnerTokenAsync();
        await PlatformScenario.ExpireInvitationAsync();
        TestApp.SetPlatformBootstrapEmail("attacker@example.test");
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue();

        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.NormalizedEmail.ShouldBe(OwnerEmail);
        invitation.TokenHash.Matches(original).ShouldBeTrue("nothing was rotated.");
    }

    /// <summary>First owner activation permanently closes bootstrap.</summary>
    [Test]
    public async Task Once_the_owner_has_activated_recovery_can_never_reopen_it()
    {
        var owner = await CompletedOwnerAsync();
        await PlatformScenario.ExpireInvitationAsync();
        PlatformScenario.RunAnonymously();

        (await RecoverAsync()).IsSuccess.ShouldBeTrue();

        (await PlatformScenario.SingleInvitationAsync()).Status.ShouldBe(PlatformAdminInvitationStatus.Accepted);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(1, "recovery activates and elevates nothing.");
        owner.ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    /// Concurrent attempts must leave exactly one current invitation and one effect set. The rotation is a
    /// conditional write on the row's concurrency token, so the losers find the state the winner produced.
    /// </summary>
    [Test]
    public async Task Concurrent_recoveries_leave_one_current_invitation_and_one_effect_set()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        await PlatformScenario.ExpireInvitationAsync();
        PlatformScenario.RunAnonymously();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RecoverAsync()));

        results.ShouldAllBe(result => result.IsSuccess);
        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(1);
        var live = (await TestApp.ListAsync<OutboxSecret>()).Where(secret => secret.Status == OutboxSecretStatus.Pending).ToArray();
        live.Length.ShouldBe(1, "exactly one token is deliverable after the dust settles.");
    }

    /// <summary>
    /// The one state-independent refusal that is allowed to be distinguishable, because a client has to be able to
    /// back off — and the SPEC requires the code and the header (IA-REQ-040, section 8).
    /// </summary>
    [Test]
    public async Task An_exhausted_budget_is_a_typed_429_with_retry_after()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        await PlatformScenario.ExpireInvitationAsync();
        PlatformScenario.RunAnonymously();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await RecoverAsync()).IsSuccess.ShouldBeTrue();
        }

        var refused = await RecoverAsync();

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("rate_limit_exceeded");
        refused.Error.RetryAfterSeconds.ShouldNotBeNull();
        refused.Error.RetryAfterSeconds!.Value.ShouldBeGreaterThan(0);
    }

    /// <summary>Over HTTP the same two refusals have to be the declared status codes, not a business answer.</summary>
    [Test]
    public async Task Over_http_a_missing_antiforgery_pair_is_a_typed_400_and_never_a_neutral_202()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        const string host = "https://platform.localhost";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/api/platform/bootstrap/recover");
        request.Headers.Add("Origin", host);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString()
            .ShouldBe("antiforgery_validation_failed");
    }

    [Test]
    public async Task Over_http_a_valid_opaque_state_is_a_neutral_202()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        const string host = "https://platform.localhost";
        var antiforgery = await IdentityHttpHarness.GetAntiforgeryAsync(harness.Client, host);

        using var request = IdentityHttpHarness.JsonRequest(HttpMethod.Post, $"{host}/api/platform/bootstrap/recover", null, antiforgery);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty("neutral means bodyless.");
    }

    private static async Task<Guid> CompletedOwnerAsync()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);
        var token = await PlatformScenario.PendingOwnerTokenAsync();
        PlatformScenario.RunAnonymously();
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.RegisterPlatformInviteeCommand(token, PlatformScenario.ValidPassword));
        var confirmation = await PlatformScenario.SealedTokenAsync(
            (await PlatformScenario.MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Invitations.ConfirmPlatformInviteeCommand(token, confirmation));

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.NormalizedEmail == OwnerEmail.ToUpperInvariant());
        PlatformScenario.RunAs(identity.Id);
        var enrollment = await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Mfa.BeginPlatformMfaEnrollmentCommand(token));
        await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Mfa.VerifyPlatformMfaEnrollmentCommand(
            token, PlatformMfaTests.TotpCode(enrollment.Value!.SharedKey)));
        (await TestApp.SendAsync(new CleanArchitecture.Application.IdentityAccess.Platform.Mfa.AcknowledgePlatformRecoveryCodesCommand(token)))
            .IsSuccess.ShouldBeTrue();
        return identity.Id;
    }
}
