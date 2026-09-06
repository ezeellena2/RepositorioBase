using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

/// <summary>
/// The registration oracle, stated as the sequence that reads it (IA-REQ-048).
/// <para>
/// Answering both anonymous probes with the same neutral <c>202</c> removed the status difference and left the
/// state difference: a probe for an unknown address reserved the CUIT and a probe for a known address reserved
/// nothing. The attacker never reads the probe's answer — they read what the system will let them do next. So
/// every test here submits one anonymous probe, then acts as an unrelated identity they control, and compares the
/// two runs. Anything that differs is the oracle.
/// </para>
/// </summary>
public sealed class RegistrationPrivacySequenceTests : TestBase
{
    private const string Password = "Testing1234!";

    /// <summary>
    /// The whole finding in one assertion. Two runs differ in one input — whether the probed address already has
    /// an account — and everything the prober can observe afterwards must be identical.
    /// </summary>
    [Test]
    public async Task An_anonymous_probe_leaves_the_same_world_behind_whether_or_not_the_address_has_an_account()
    {
        var known = await ProbeThenClaimAsync(addressAlreadyHasAnAccount: true);
        await TestApp.ResetState();
        var unknown = await ProbeThenClaimAsync(addressAlreadyHasAnAccount: false);

        known.ProbeSucceeded.ShouldBe(unknown.ProbeSucceeded, "the probe's own answer is already neutral.");
        known.ClaimErrorCode.ShouldBe(
            unknown.ClaimErrorCode,
            "the attacker claims the probed CUIT with their own identity; a different answer reads out whether the address had an account.");
        known.AttackerOwnsTheOrganization.ShouldBe(
            unknown.AttackerOwnsTheOrganization,
            "owning the organization in one run and not the other is the same oracle, read from the attacker's context instead of the status.");
        known.OrganizationsForTheCuit.ShouldBe(
            unknown.OrganizationsForTheCuit,
            "a durable CUIT claim left by an unproven request is what makes the two runs distinguishable.");
    }

    /// <summary>
    /// The same comparison stated as the invariant, so a future change that makes both runs fail equally cannot
    /// pass by breaking registration for everybody: the attacker's claim must actually succeed in both runs.
    /// </summary>
    [Test]
    public async Task An_unproven_probe_reserves_no_cuit_and_refuses_nobody()
    {
        foreach (var addressHasAnAccount in new[] { true, false })
        {
            var run = await ProbeThenClaimAsync(addressHasAnAccount);

            run.ProbeSucceeded.ShouldBeTrue();
            run.ClaimErrorCode.ShouldBeNull($"the probe must refuse nobody (address had an account: {addressHasAnAccount}).");
            run.AttackerOwnsTheOrganization.ShouldBeTrue();
            run.OrganizationsForTheCuit.ShouldBe(1);
            await TestApp.ResetState();
        }
    }

    /// <summary>An unproven probe creates no identity for the address it names, whatever else it writes.</summary>
    [Test]
    public async Task An_anonymous_probe_creates_no_identity_and_no_tenant()
    {
        var email = NewEmail("probe");
        RunAnonymously();

        var probe = await TestApp.SendAsync(new RegisterOrganizationCommand(email, Password, "Probe Organization", NewCuit()));

        probe.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0, "an address nobody proved control of gets no account.");
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    /// <summary>
    /// What the probe does write is one intent, one message and one audit record — the same count for a known and
    /// an unknown address, so the row count itself is not a second oracle.
    /// </summary>
    [Test]
    public async Task An_anonymous_probe_writes_one_intent_and_one_message_for_either_address()
    {
        foreach (var addressHasAnAccount in new[] { true, false })
        {
            var email = NewEmail("intent");
            if (addressHasAnAccount) await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);
            RunAnonymously();

            (await TestApp.SendAsync(new RegisterOrganizationCommand(email, Password, "Intent Organization", NewCuit())))
                .IsSuccess.ShouldBeTrue();

            (await TestApp.CountAsync<PendingRegistrationIntent>())
                .ShouldBe(1, $"one intent per probe (address had an account: {addressHasAnAccount}).");
            (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
            (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1);
            await TestApp.ResetState();
        }
    }

    /// <summary>One probe, one attacker, and everything the attacker can observe afterwards.</summary>
    private static async Task<SequenceObservation> ProbeThenClaimAsync(bool addressAlreadyHasAnAccount)
    {
        var probedEmail = NewEmail("probed");
        if (addressAlreadyHasAnAccount) await IdentityHttpHarness.SeedConfirmedUserAsync(probedEmail, Password);

        var cuit = NewCuit();
        RunAnonymously();
        var probe = await TestApp.SendAsync(new RegisterOrganizationCommand(probedEmail, Password, "Probed Organization", cuit));

        // The attacker is an ordinary confirmed identity of their own, which is all the sequence needs.
        var attackerEmail = NewEmail("attacker");
        var attackerId = await TestApp.RunAsUserAsync(attackerEmail, Password, []);
        TestApp.SetValidatedOptionalSession(attackerId, attackerEmail);
        var claim = await TestApp.SendAsync(new RegisterOrganizationCommand(attackerEmail, Password, "Attacker Organization", cuit));

        var profiles = await TestApp.ListAsync<OrganizationProfile>();
        var memberships = await TestApp.ListAsync<TenantMembership>();
        return new SequenceObservation(
            probe.IsSuccess,
            claim.IsSuccess ? null : claim.Error!.Code,
            memberships.Any(membership => membership.IdentityId == attackerId),
            profiles.Count(profile => profile.Cuit == NormalizedCuit.From(cuit)));
    }

    private static void RunAnonymously()
    {
        TestApp.SetUserId(null);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(false);
        TestApp.SetValidatedOptionalSession(null, null);
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>A CUIT nobody has registered, so each run starts from the same free value.</summary>
    private static string NewCuit() => IdentityAccessCuit.Next();

    private sealed record SequenceObservation(
        bool ProbeSucceeded,
        string? ClaimErrorCode,
        bool AttackerOwnsTheOrganization,
        int OrganizationsForTheCuit);
}

/// <summary>Distinct valid CUIT values, so two runs never collide by accident.</summary>
internal static class IdentityAccessCuit
{
    private static int _next;

    internal static string Next()
    {
        var serial = Interlocked.Increment(ref _next) % 90_000_000 + 10_000_000;
        return $"30-{serial:D8}-9";
    }
}
