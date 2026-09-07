using System.Net;
using System.Text.Json;
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
        expected.Length.ShouldBeGreaterThan(100);
        var path = $"/api/tenants/{organization.TenantId.Value}/{directory}";
        using var first = await organization.Owner.GetAsync(path);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstPage = await IdentityHttpHarness.ReadJsonAsync(first);
        var firstIds = Ids(firstPage, key);
        firstIds.Length.ShouldBe(100, "the existing default page size remains unchanged");
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNullOrWhiteSpace();

        using var second = await organization.Owner.GetAsync($"{path}?cursor={Uri.EscapeDataString(cursor!)}");
        TestContext.Out.WriteLine($"R6C {directory}: seeded={expected.Length}; first={(int)first.StatusCode}, items={firstIds.Length}, nextCursor present; continuation={(int)second.StatusCode}");
        if (second.StatusCode == HttpStatusCode.InternalServerError)
            await CaptureContinuationFailureAsync(organization, directory, cursor!);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var secondPage = await IdentityHttpHarness.ReadJsonAsync(second);
        var secondIds = Ids(secondPage, key);
        firstIds.Intersect(secondIds).ShouldBeEmpty("stable data must not repeat a record across page boundaries");
        firstIds.Concat(secondIds).ShouldBe(expected, ignoreOrder: true);
        secondPage.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static Guid[] Ids(JsonElement page, string key) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty(key).GetGuid()).ToArray();

    private static async Task CaptureContinuationFailureAsync(OrganizationScenario organization, string directory, string cursor)
    {
        // The HTTP boundary correctly hides exception details. Read the same scoped store to identify the failed
        // query without adding a diagnostic endpoint or relaxing the expected HTTP assertion.
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        try
        {
            if (directory == "roles")
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Roles.IRoleAdministrationStore>()
                    .ListAsync(organization.TenantId, 0, cursor, CancellationToken.None);
            else if (directory == "members")
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Members.IMembershipAdministrationStore>()
                    .ListAsync(organization.TenantId, 0, cursor, CancellationToken.None);
            else
                await scope.ServiceProvider.GetRequiredService<Application.IdentityAccess.Members.IMembershipAdministrationStore>()
                    .ListInvitationsAsync(organization.TenantId, 0, cursor, CancellationToken.None);
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
        for (var index = 0; index < 105; index++)
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
