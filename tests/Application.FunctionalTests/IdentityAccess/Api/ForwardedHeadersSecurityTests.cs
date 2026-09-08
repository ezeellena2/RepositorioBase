using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

public sealed class ForwardedHeadersSecurityTests : TestBase
{
    [Test]
    public async Task Trusted_loopback_proxy_can_supply_the_external_https_origin_for_antiforgery()
    {
        const string externalHost = "public-proxy.localhost";
        using var bootstrap = new HttpRequestMessage(HttpMethod.Get, "http://internal.localhost/api/identity/antiforgery");
        bootstrap.Headers.Add("X-Forwarded-Proto", "https");
        bootstrap.Headers.Add("X-Forwarded-Host", externalHost);
        var bootstrapResponse = await FunctionalTestSetup.HttpClient.SendAsync(bootstrap);
        bootstrapResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await bootstrapResponse.Content.ReadFromJsonAsync<CleanArchitecture.Web.Endpoints.AntiforgeryResponse>())!.RequestToken;
        var cookie = bootstrapResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var confirmation = new HttpRequestMessage(HttpMethod.Post, "http://internal.localhost/api/identity/confirm-email")
        {
            Content = JsonContent.Create(new { token = "unknown-confirmation-token" })
        };
        confirmation.Headers.Add("X-Forwarded-Proto", "https");
        confirmation.Headers.Add("X-Forwarded-Host", externalHost);
        confirmation.Headers.Add("Origin", $"https://{externalHost}");
        confirmation.Headers.Add("X-CSRF-TOKEN", token);
        confirmation.Headers.Add("Cookie", cookie);

        var response = await FunctionalTestSetup.HttpClient.SendAsync(confirmation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_confirmation");
    }

    [Test]
    public async Task Only_known_one_hop_loopback_proxies_are_trusted_for_forwarded_headers()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var configured = scope.ServiceProvider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        configured.ForwardLimit.ShouldBe(1);
        configured.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);
        configured.KnownIPNetworks.ShouldBeEmpty();
        configured.KnownProxies.ShouldContain(IPAddress.Loopback);
        configured.KnownProxies.ShouldContain(IPAddress.IPv6Loopback);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("internal.localhost");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "spoofed.localhost";

        await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(configured)).Invoke(context);

        context.Request.Scheme.ShouldBe("http");
        context.Request.Host.Host.ShouldBe("internal.localhost");
    }

    /// <summary>
    /// Every control this system has over authentication ends at the browser, and none of what happens there is
    /// decided by a handler — so none of it can be checked by asking one. These are asked of a real response.
    /// </summary>
    [TestCase("/api/identity/antiforgery")]
    [TestCase("/index.html")]
    public async Task Every_response_carries_the_headers_a_session_cookie_depends_on(string path)
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://headers.localhost{path}");

        var response = await harness.Client.SendAsync(request);

        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "Referrer-Policy").ShouldBe("no-referrer");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        var policy = Header(response, "Content-Security-Policy");
        policy.ShouldContain("frame-ancestors 'none'", customMessage: "a framed page is a stealable session");
        policy.ShouldContain("script-src 'self'", customMessage: "the half of the policy that stops injection");
        policy.ShouldContain("form-action 'self'", customMessage: "an antiforgery token must not be postable elsewhere");
        policy.ShouldContain("object-src 'none'");
        policy.ShouldContain("base-uri 'self'");
    }

    /// <summary>
    /// An API response is an answer about one caller. A shared cache holding one is that caller's session handed
    /// to the next person through it, which is why this one is not left to the handler that wrote the body.
    /// </summary>
    [Test]
    public async Task An_api_response_is_never_stored_and_never_indexed()
    {
        using var harness = IdentityHttpHarness.CreateProductionHarness();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://headers.localhost/api/identity/antiforgery");

        var response = await harness.Client.SendAsync(request);

        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        Header(response, "X-Robots-Tag").ShouldContain("noindex");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : response.Content.Headers.TryGetValues(name, out var contentValues)
                ? string.Join(", ", contentValues)
                : throw new AssertionException($"The response carried no {name} header.");
}
