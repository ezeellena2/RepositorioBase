using System.Text.Json;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// Every invitation route is contractual. They are asserted against the document the application actually serves
/// rather than a checked-in artifact, and the negatives matter as much as the positives: an invitation route
/// mounted under the identity self-service prefix, or a preview route left behind, would each be a surface
/// nobody declared.
/// <para>
/// A Platform invitation is a different offer from an organization one and lives under its own prefix, so the
/// exhaustive list spans both: adding either kind without declaring it here fails.
/// </para>
/// </summary>
public sealed class InvitationRouteContractTests : TestBase
{
    [TestCase("/api/tenants/{tenantId}/invitations")]
    [TestCase("/api/invitations/register")]
    [TestCase("/api/invitations/accept")]
    [TestCase("/api/platform/invitations/register")]
    [TestCase("/api/platform/invitations/confirm")]
    public async Task Invitation_routes_are_declared_exactly_as_specified(string path)
    {
        var paths = await PathsAsync();

        paths.TryGetProperty(path, out var route).ShouldBeTrue($"{path} must be declared.");
        route.TryGetProperty("post", out _).ShouldBeTrue($"{path} is a POST route.");
    }

    [Test]
    public async Task No_invitation_route_is_mounted_under_the_identity_self_service_prefix()
    {
        var declared = await DeclaredPathsAsync();

        declared.ShouldNotContain(
            path => path.StartsWith("/api/identity/invitations", StringComparison.OrdinalIgnoreCase),
            "invitations are reached before an identity has any tenant context, so they never live under /api/identity.");
    }

    [Test]
    public async Task No_preview_or_undeclared_invitation_route_is_exposed()
    {
        var declared = await DeclaredPathsAsync();

        declared.ShouldNotContain(path => path.Contains("preview", StringComparison.OrdinalIgnoreCase));
        declared
            .Where(path => path.Contains("invitation", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ShouldBe([
                "/api/invitations/accept",
                "/api/invitations/register",
                "/api/platform/invitations/confirm",
                "/api/platform/invitations/register",
                "/api/tenants/{tenantId}/invitations"
            ]);
    }

    private static async Task<JsonElement> PathsAsync()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("paths").Clone();
    }

    private static async Task<string[]> DeclaredPathsAsync() =>
        (await PathsAsync()).EnumerateObject().Select(path => path.Name).ToArray();
}
