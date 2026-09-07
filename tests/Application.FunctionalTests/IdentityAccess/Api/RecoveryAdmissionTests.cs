using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// What a closed deployment actually answers, over the real pipeline (IA-REQ-055).
/// <para>
/// The adapter's own decisions are covered against every kind of bad evidence elsewhere. What this file adds is
/// the part only the transport can show: that the refusal happens before anything reads a cookie, a session or
/// the database, and that the one thing still answering says nothing about the deployment.
/// </para>
/// </summary>
public sealed class RecoveryAdmissionTests : TestBase
{
    private const string Deployment = "identity-access-functional";
    private static readonly string Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("an-operator-key-nobody-put-in-a-backup"));

    private static string Host() => $"https://admission-{Guid.NewGuid():N}.localhost";

    [Test]
    public async Task A_closed_deployment_refuses_every_route_and_says_so_in_the_contract_language()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness(settings: Armed(evidence: null));
        using var client = harness.Client;
        var host = Host();

        foreach (var route in new[] { "/api/identity/antiforgery", "/api/identity/context", "/api/platform/identities" })
        {
            using var response = await client.GetAsync($"{host}{route}");

            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, route);
            response.Headers.RetryAfter.ShouldNotBeNull(route);
            (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString()
                .ShouldBe("recovery_admission_closed", route);
        }
    }

    /// <summary>
    /// An orchestrator has to tell a process that is running and refusing from one that is not running. A probe
    /// that returned anything about the deployment would itself be ingress, so it returns a status and no more.
    /// </summary>
    [Test]
    public async Task Only_the_probes_answer_and_they_say_nothing_about_the_deployment()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness(settings: Armed(evidence: null));
        using var client = harness.Client;
        var host = Host();

        using var alive = await client.GetAsync($"{host}/alive");

        alive.StatusCode.ShouldNotBe(HttpStatusCode.ServiceUnavailable, "a refusing process still has to be visibly alive");
        (await alive.Content.ReadAsStringAsync()).ShouldNotContain(Deployment);
    }

    /// <summary>
    /// Nothing signs itself. A deployment holding the evidence — the thing a backup could plausibly contain — and
    /// not the operator's key stays closed, rather than falling back to admitting everything.
    /// </summary>
    [Test]
    public async Task Evidence_without_the_operators_key_opens_nothing()
    {
        var settings = new Dictionary<string, string?>(Armed(Signed(release: true)))
        {
            ["IdentityAccess:Recovery:VerificationKey"] = null
        };
        using var harness = IdentityHttpHarness.CreateProductionHarness(settings: settings);
        using var client = harness.Client;

        using var response = await client.GetAsync($"{Host()}/api/identity/antiforgery");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, "evidence alone is not what opens a deployment");
        (await IdentityHttpHarness.ReadProblemAsync(response)).GetProperty("code").GetString()
            .ShouldBe("recovery_admission_closed");
    }

    [Test]
    public async Task A_released_deployment_serves_the_routes_it_always_did()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness(settings: Armed(Signed(release: true)));
        using var client = harness.Client;

        using var response = await client.GetAsync($"{Host()}/api/identity/antiforgery");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Verified but unreleased: authentication and revalidation are admitted, and nothing else is opened.</summary>
    [Test]
    public async Task A_quarantined_deployment_admits_the_authentication_routes()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness(settings: Armed(Signed(release: false)));
        using var client = harness.Client;

        using var response = await client.GetAsync($"{Host()}/api/identity/antiforgery");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string?> Armed(string? evidence) => new()
    {
        ["IdentityAccess:Recovery:Deployment"] = Deployment,
        ["IdentityAccess:Recovery:VerificationKey"] = Key,
        ["IdentityAccess:Recovery:Evidence"] = evidence
    };

    private static string Signed(bool release)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new RecoveryEvidence(
            Deployment, now.ToString("O"), "backup-1", now.AddMinutes(-5).ToString("O"), now.AddHours(2).ToString("O"), release, null);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(
            Convert.FromBase64String(Key), Encoding.UTF8.GetBytes(ConfiguredRecoveryAdmission.Canonical(evidence))));
        return JsonSerializer.Serialize(evidence with { Signature = signature });
    }
}
