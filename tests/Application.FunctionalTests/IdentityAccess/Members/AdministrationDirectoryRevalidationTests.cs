using System.Net;
using System.Text.Json;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>R6B/R6C: independent read permissions and actual continuation over more than the default page.</summary>
public sealed class AdministrationDirectoryRevalidationTests : TestBase
{
    [Test]
    public async Task R6B_Members_read_alone_can_list_members_without_authorizing_role_reads()
    {
        using var organization = await OrganizationScenario.CreateAsync("r6b");
        var reader = await organization.AddMemberAsync("reader", Permissions.MembersRead);
        using var members = await reader.Acting.GetAsync($"/api/tenants/{organization.TenantId.Value}/members");
        using var roles = await reader.Acting.GetAsync($"/api/tenants/{organization.TenantId.Value}/roles");

        TestContext.Out.WriteLine($"R6B members={(int)members.StatusCode}; roles={(int)roles.StatusCode}");
        members.StatusCode.ShouldBe(HttpStatusCode.OK, await members.Content.ReadAsStringAsync());
        roles.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await IdentityHttpHarness.ReadJsonAsync(members)).GetProperty("items").GetArrayLength().ShouldBe(2);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IEffectivePermissionReader>()
            .GetEffectivePermissionsAsync(reader.IdentityId, organization.TenantId)).ShouldBe([Permissions.MembersRead]);
    }

    [TestCase("roles", "roleId")]
    [TestCase("members", "membershipId")]
    [TestCase("invitations", "invitationId")]
    public async Task R6C_The_next_page_is_executable_and_omits_or_duplicates_no_records(string directory, string key)
    {
        using var organization = await OrganizationScenario.CreateAsync($"r6c-{directory}");
        var expected = await SeedDirectoryAsync(organization, directory);
        expected.Length.ShouldBe(DirectorySize, "the smallest directory that spans two pages at the default page size");
        var path = $"/api/tenants/{organization.TenantId.Value}/{directory}";

        using var first = await organization.Owner.GetAsync(path);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstPage = await IdentityHttpHarness.ReadJsonAsync(first);
        var firstIds = Ids(firstPage, key);
        firstIds.Length.ShouldBe(25, "an omitted page is the first page at the default page size of 25");
        PageMember(firstPage, "pageSize").GetInt32().ShouldBe(25);
        PageMember(firstPage, "totalCount").GetInt32().ShouldBe(DirectorySize);
        PageMember(firstPage, "hasNextPage").GetBoolean().ShouldBeTrue();

        using var second = await organization.Owner.GetAsync($"{path}?pageNumber=2&pageSize=25");
        TestContext.Out.WriteLine($"R6C {directory}: seeded={expected.Length}; first={(int)first.StatusCode}, items={firstIds.Length}; page 2={(int)second.StatusCode}");
        if (second.StatusCode == HttpStatusCode.InternalServerError)
            await CaptureContinuationFailureAsync(organization, directory);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var secondPage = await IdentityHttpHarness.ReadJsonAsync(second);
        var secondIds = Ids(secondPage, key);
        secondIds.Length.ShouldBe(1, "the second page holds the last row");
        PageMember(secondPage, "hasNextPage").GetBoolean().ShouldBeFalse();
        firstIds.Intersect(secondIds).ShouldBeEmpty("stable data must not repeat a record across page boundaries");
        firstIds.Concat(secondIds).ShouldBe(expected, ignoreOrder: true);

        using var whole = await organization.Owner.GetAsync($"{path}?pageSize=100");
        whole.StatusCode.ShouldBe(HttpStatusCode.OK, await whole.Content.ReadAsStringAsync());
        firstIds.Concat(secondIds).ShouldBe(Ids(await IdentityHttpHarness.ReadJsonAsync(whole), key),
            "consecutive pages keep the order of one larger page");
    }

    /// <summary>The smallest directory that spans two pages at the default page size (D12).</summary>
    private const int DirectorySize = 26;

    private static Guid[] Ids(JsonElement page, string key) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty(key).GetGuid()).ToArray();

    private static JsonElement PageMember(JsonElement page, string name)
    {
        page.TryGetProperty(name, out var value).ShouldBeTrue($"the page must carry {name}");
        return value;
    }

    private static async Task CaptureContinuationFailureAsync(OrganizationScenario organization, string directory)
    {
        // The HTTP boundary correctly hides exception details. Read the same scoped store to identify the failed
        // query without adding a diagnostic endpoint or relaxing the expected HTTP assertion.
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        try
        {
            if (directory == "roles")
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Roles.IRoleAdministrationStore>()
                    .ListAsync(organization.TenantId, new PaginationQuery(2, 25), CancellationToken.None);
            else if (directory == "members")
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Members.IMembershipAdministrationStore>()
                    .ListAsync(organization.TenantId, new PaginationQuery(2, 25), CancellationToken.None);
            else
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Members.IMembershipAdministrationStore>()
                    .ListInvitationsAsync(organization.TenantId, new PaginationQuery(2, 25), CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            TestContext.Out.WriteLine($"R6C {directory} store failure: {exception.Message}");
        }
    }

    private static async Task<Guid[]> SeedDirectoryAsync(OrganizationScenario organization, string directory)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == organization.TenantId);
        var ownerRole = await context.TenantRoles.SingleAsync(role => role.TenantId == tenant.Id && role.IsSystem);
        // The scenario already holds the owner's system role and membership, so they count toward the size.
        var existing = directory switch
        {
            "roles" => await context.TenantRoles.CountAsync(role => role.TenantId == tenant.Id),
            "members" => await context.TenantMemberships.CountAsync(member => member.TenantId == tenant.Id),
            _ => await context.Invitations.CountAsync(invitation => invitation.TenantId == tenant.Id)
        };
        for (var index = 0; index < DirectorySize - existing; index++)
        {
            if (directory == "roles")
            {
                context.TenantRoles.Add(Role.Create(tenant, $"Fictional role {index:D3}"));
            }
            else if (directory == "members")
            {
                var email = $"fictional-{index:D3}-{Guid.NewGuid():N}@example.test";
                var identity = new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true
                };
                var member = TenantMembership.CreateInvited(tenant, identity.Id);
                member.Activate(tenant);
                context.AddRange(identity, member);
            }
            else
            {
                context.Invitations.Add(Invitation.Issue(tenant, $"fictional-{index:D3}@example.test", [ownerRole],
                    VersionedTokenHash.Of($"fictional-token-{Guid.NewGuid():N}"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
            }
        }

        await context.SaveChangesAsync();
        return directory switch
        {
            "roles" => await context.TenantRoles.Where(role => role.TenantId == tenant.Id).Select(role => role.Id.Value).ToArrayAsync(),
            "members" => await context.TenantMemberships.Where(member => member.TenantId == tenant.Id).Select(member => member.Id.Value).ToArrayAsync(),
            _ => await context.Invitations.Where(invitation => invitation.TenantId == tenant.Id).Select(invitation => invitation.Id.Value).ToArrayAsync()
        };
    }
}
