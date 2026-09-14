using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The directory contract (IA-REQ-045): four distinct <c>/api/platform/*</c> resources, each bounded, each with
/// its own typed offset page — and no change to the identity endpoints, which are required not to have a
/// pagination envelope at all.
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
        parameters.ShouldBe(["pageNumber", "pageSize"], ignoreOrder: true, customMessage: $"{path} takes a page, not a filter.");
    }

    /// <summary>
    /// Four types rather than one shared envelope. A shared one is exactly the pagination wrapper the identity
    /// endpoints must not have, and sharing it would make widening one directory widen all four.
    /// </summary>
    [TestCase("/api/platform/organizations", "PlatformOrganizationDirectoryResponse")]
    [TestCase("/api/platform/identities", "PlatformIdentityDirectoryResponse")]
    [TestCase("/api/platform/admins", "PlatformAdministratorDirectoryResponse")]
    [TestCase("/api/platform/audit", "PlatformAuditDirectoryResponse")]
    public async Task Each_directory_returns_its_own_typed_offset_page(string path, string schema)
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
                    name.ShouldNotBe("pageNumber", $"{path.Name} must not become a paged resource.");
                    name.ShouldNotBe("pageSize", $"{path.Name} must not become a paged resource.");
                }
            }
        }
    }

    /// <summary>
    /// A caller may ask for a page and never for everything. The page size is clamped rather than refused, because
    /// a size outside the range is a client bug and not a security event — what matters is that it is bounded.
    /// </summary>
    [Test]
    public async Task A_page_size_beyond_the_bounds_is_clamped_rather_than_honoured()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(4);

        var huge = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(1, 10_000)))).Value!;
        var zero = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(1, 0)))).Value!;

        huge.Items.Count.ShouldBe(4, "there are only four; the page size is bounded, not obeyed.");
        huge.PageSize.ShouldBe(PaginationQuery.MaxPageSize);
        zero.Items.Count.ShouldBe(1, "a page size below the minimum still returns a page of one row.");
        zero.PageSize.ShouldBe(1);
    }

    /// <summary>
    /// The audit directory is a log, so it is ordered by when things happened rather than by a row identifier.
    /// Ordering it by a random UUID would put a new event in an arbitrary position, which makes a bounded page
    /// an arbitrary slice of history instead of the most recent one.
    /// </summary>
    [Test]
    public async Task The_audit_directory_is_newest_first_and_pages_backwards_in_time()
    {
        await PlatformScenario.ActiveOwnerAsync();

        var first = (await TestApp.SendAsync(new ListPlatformAuditQuery(new PaginationQuery(1, 2)))).Value!;
        first.Items.Count.ShouldBe(2);
        first.Items[0].OccurredAtUtc.ShouldBeGreaterThanOrEqualTo(first.Items[1].OccurredAtUtc);

        var second = (await TestApp.SendAsync(new ListPlatformAuditQuery(new PaginationQuery(2, 2)))).Value!;
        second.Items.ShouldNotBeEmpty("the owner's ceremony leaves more than one page of audit history at size 2.");
        var oldestOnFirstPage = first.Items[^1].OccurredAtUtc;
        second.Items.ShouldAllBe(item => item.OccurredAtUtc <= oldestOnFirstPage);
        second.Items.Select(item => item.EventId).Intersect(first.Items.Select(item => item.EventId))
            .ShouldBeEmpty("consecutive pages do not overlap.");
    }

    private static readonly string[] ListPaths =
    [
        "/api/platform/organizations", "/api/platform/identities", "/api/platform/admins", "/api/platform/audit",
        "/api/tenants/{tenantId}/roles", "/api/tenants/{tenantId}/members", "/api/tenants/{tenantId}/invitations",
    ];

    [Test]
    public async Task Every_list_route_declares_offset_parameters_and_its_binding_refusal()
    {
        var paths = await PathsAsync();
        foreach (var path in ListPaths)
        {
            var get = paths.GetProperty(path).GetProperty("get");
            var names = get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
            names.ShouldContain("pageNumber", path);
            names.ShouldContain("pageSize", path);
            names.ShouldNotContain("limit", path);
            names.ShouldNotContain("cursor", path);
            get.GetProperty("responses").GetProperty("400").GetProperty("x-problem-codes").EnumerateArray()
                .Select(code => code.GetString()).ShouldContain("invalid_request", path);
        }
    }

    [Test]
    public async Task Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(3);
        TestApp.ResetCapturedLogs();

        var huge = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(int.MaxValue, int.MaxValue)));
        var zero = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(0, 0)));

        huge.IsSuccess.ShouldBeTrue();
        huge.Value!.PageNumber.ShouldBe(PaginationQuery.MaxPageNumber);
        huge.Value.PageSize.ShouldBe(PaginationQuery.MaxPageSize);
        huge.Value.Items.ShouldBeEmpty();
        zero.Value!.PageNumber.ShouldBe(1);
        zero.Value.PageSize.ShouldBe(1);
        zero.Value.Items.Count.ShouldBe(1, "a zero page size returns one row.");
        TestApp.CapturedLogs.ShouldNotContain(entry => entry.StartsWith("[Error] CleanArchitecture.", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationsAsync(3);

        var result = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(5, 2)));

        result.IsSuccess.ShouldBeTrue("a page past the end is not a missing resource (E4)");
        result.Value!.Items.ShouldBeEmpty();
        result.Value.PageNumber.ShouldBe(5);
        result.Value.TotalCount.ShouldBeGreaterThanOrEqualTo(3);
        result.Value.TotalPages.ShouldBe((int)Math.Ceiling(result.Value.TotalCount / 2d));
        result.Value.HasNextPage.ShouldBeFalse();
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
