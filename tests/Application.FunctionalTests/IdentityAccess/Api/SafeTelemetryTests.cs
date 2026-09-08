using System.Net;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;
using static CleanArchitecture.Application.FunctionalTests.Infrastructure.IdentityHttpHarness;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// What must never end up in a log, an exception message or a metric label (IA-REQ-029, IA-REQ-032).
/// <para>
/// Diagnostics outlive the request and travel further than it does: to a file, to an aggregator, to whoever is on
/// call. A provider callback code, a session handle, a document number and an address are each enough to become
/// somebody by themselves, and none of them is worth what it costs to have written it down. Everything an
/// operator actually needs — that a sign-in failed, that a budget refused, that an envelope could not be opened —
/// is a count and a reason, both of which are here already.
/// </para>
/// <para>
/// The capture is process-wide and every log level is captured, structured values included, so a value formatted
/// into a message and a value attached as a property are both caught.
/// </para>
/// </summary>
public sealed class SafeTelemetryTests : TestBase
{
    [Test]
    public async Task A_failed_sign_in_writes_no_address_and_no_password_anywhere_diagnostics_can_reach()
    {
        const string host = "https://telemetry-signin.localhost";
        const string email = "Telemetry.Person@Example.Test";
        const string password = "a-password-nobody-should-log";
        const string address = "198.51.100.44";
        using var harness = CreateProductionHarness();
        var antiforgery = await GetAntiforgeryAsync(harness.Client, host);
        TestApp.ResetCapturedLogs();

        using var request = LoginRequest(host, email, password, antiforgery, address);
        (await harness.Client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        NothingLogged(witness: "CreateSessionCommand", "telemetry.person", password, address);
    }

    [Test]
    public async Task A_session_handle_never_appears_in_diagnostics()
    {
        const string host = "https://telemetry-session.localhost";
        var email = $"telemetry-session-{Guid.NewGuid():N}@example.test";
        await SeedConfirmedUserAsync(email, PlatformScenarioPassword);
        using var harness = CreateProductionHarness();
        TestApp.ResetCapturedLogs();

        var cookie = await SignInAsync(harness.Client, host, email, PlatformScenarioPassword);
        (await harness.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{host}/api/identity/sessions")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The cookie value is the session. A log line holding one is a log line anybody who reads it can sign in with.
        NothingLogged(witness: "ListOwnSessionsQuery", cookie.Split('=', 2)[1]);
    }

    [Test]
    public async Task A_document_number_never_appears_in_diagnostics_even_when_the_claim_is_refused()
    {
        const string document = "27182818";
        var identityId = await SeedConfirmedUserAsync($"telemetry-claim-{Guid.NewGuid():N}@example.test", PlatformScenarioPassword);
        Platform.PlatformScenario.RunAs(identityId);
        TestApp.ResetCapturedLogs();

        // Twice: the first is accepted and the second is the conflict, so both the success path and the refusal
        // path have had the number in hand.
        await TestApp.SendAsync(new CreatePersonalContextCommand("Telemetry Person", "Telemetry", document));
        await TestApp.SendAsync(new CreatePersonalContextCommand("Telemetry Person", "Telemetry", document));

        NothingLogged(witness: "CreatePersonalContextCommand", document);
    }

    private const string PlatformScenarioPassword = "Sup3rS3cret!Passw0rd";

    /// <summary>
    /// <paramref name="witness"/> is something the operation under test is known to log. Without it these tests
    /// would pass just as well against a capture that had missed the request entirely, which is the one way an
    /// absence check can be worth nothing.
    /// </summary>
    private static void NothingLogged(string witness, params string[] secrets)
    {
        var logs = TestApp.CapturedLogs;
        logs.ShouldContain(entry => entry.Contains(witness, StringComparison.Ordinal),
            $"nothing in the capture mentions {witness}, so this test would pass against a capture of nothing");
        foreach (var secret in secrets)
        {
            logs.Where(entry => entry.Contains(secret, StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"'{secret}' reached a log, and a log travels further than the request that made it");
        }
    }
}
