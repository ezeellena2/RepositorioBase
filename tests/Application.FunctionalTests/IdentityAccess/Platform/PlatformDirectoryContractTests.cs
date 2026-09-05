using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The directory contract (IA-REQ-045): four distinct <c>/api/platform/*</c> resources, each bounded, each with
/// its own typed <c>items</c>/<c>nextCursor</c> — and no change to the identity endpoints, which are required not
/// to have a pagination envelope at all.
/// </summary>
public sealed class PlatformDirectoryContractTests : TestBase
{
    [TestCase("/api/platform/organizations")]
    [TestCase("/api/platform/identities")]
    [TestCase("/api/platform/admins")]
    [TestCase("/api/platform/audit")]
    public async Task Each_directory_is_declared_as_a_bounded_get(string path)
    {
        var paths = await PathsAsync();

        paths.TryGetProperty(path, out var route).ShouldBeTrue($"{path} must be declared.");
        route.TryGetProperty("get", out var get).ShouldBeTrue($"{path} is a read.");
        var parameters = get.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        parameters.ShouldBe(["limit", "cursor"], ignoreOrder: true, customMessage: $"{path} takes a page, not a filter.");
    }

    /// <summary>
    /// Four types rather than one shared envelope. A shared one is exactly the pagination wrapper the identity
    /// endpoints must not have, and sharing it would make widening one directory widen all four.
    /// </summary>
    [TestCase("/api/platform/organizations", "PlatformOrganizationDirectoryResponse")]
    [TestCase("/api/platform/identities", "PlatformIdentityDirectoryResponse")]
    [TestCase("/api/platform/admins", "PlatformAdministratorDirectoryResponse")]
    [TestCase("/api/platform/audit", "PlatformAuditDirectoryResponse")]
    public async Task Each_directory_returns_its_own_typed_items_and_next_cursor(string path, string schema)
    {
        var paths = await PathsAsync();

        var reference = paths.GetProperty(path).GetProperty("get")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();

        reference.ShouldBe($"#/components/schemas/{schema}");
    }

    /// <summary>The Platform directories change nothing about the identity surface (IA-REQ-045).</summary>
    [Test]
    public async Task No_identity_endpoint_gained_a_pagination_envelope()
    {
        var document = await DocumentAsync();
        var paths = document.GetProperty("paths");

        foreach (var path in paths.EnumerateObject().Where(entry => entry.Name.StartsWith("/api/identity/", StringComparison.Ordinal)))
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("parameters", out var parameters)) continue;
                foreach (var parameter in parameters.EnumerateArray())
                {
                    var name = parameter.GetProperty("name").GetString();
                    name.ShouldNotBe("limit", $"{path.Name} must not become a paged resource.");
                    name.ShouldNotBe("cursor", $"{path.Name} must not become a paged resource.");
                }
            }
        }
    }

    /// <summary>
    /// A caller may ask for a page and never for everything. The limit is clamped rather than refused, because a
    /// limit outside the range is a client bug and not a security event — what matters is that it is bounded.
    /// </summary>
    [Test]
    public async Task A_limit_beyond_the_bounds_is_clamped_rather_than_honoured()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(4);

        var huge = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(10_000, null)))).Value!;
        var zero = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(0, null)))).Value!;

        huge.Items.Count.ShouldBe(4, "there are only four; the limit is bounded, not obeyed.");
        zero.Items.Count.ShouldBe(1, "a limit below the minimum still returns a page.");
    }

    /// <summary>
    /// The cursor walks the whole set exactly once and stops. That is what makes it a cursor rather than an
    /// offset: nothing is repeated and nothing is skipped.
    /// </summary>
    [Test]
    public async Task The_cursor_walks_every_row_once_and_then_stops()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(5);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(2, cursor)))).Value!;
            page.Items.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(page.Items.Select(item => item.TenantId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Count.ShouldBe(5);
        seen.Distinct().Count().ShouldBe(5, "no row is returned twice.");
    }

    /// <summary>A cursor the server did not issue starts from the beginning rather than becoming a probe.</summary>
    [Test]
    public async Task A_cursor_the_server_never_issued_is_not_a_way_to_ask_for_something_else()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(3);

        var forged = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(25, "not-a-cursor")))).Value!;

        forged.Items.Count.ShouldBe(3);
    }

    private static async Task OrganizationsAsync(int count)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var index = 0; index < count; index++)
        {
            var tenant = Tenant.CreateOrganization(TenantSlug.From($"acme-{Guid.NewGuid():N}"));
            tenant.Activate();
            context.Add(tenant);
        }

        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> DocumentAsync()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> PathsAsync() => (await DocumentAsync()).GetProperty("paths");
}
