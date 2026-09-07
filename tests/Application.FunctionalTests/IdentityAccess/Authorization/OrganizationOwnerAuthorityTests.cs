using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Authorization;

/// <summary>
/// What the person who registered an Organization can actually do in it (IA-REQ-053, amendment D1).
/// <para>
/// It exists because the answer used to be "nothing". The `Owner` role was created and assigned and given no
/// permissions at all, so a real registered owner could not invite anybody — and no test noticed, because every
/// test that needed a permission granted itself one directly. C5's grant ceiling makes that state unworkable
/// rather than merely awkward: an actor may only grant what it holds, so an owner holding nothing can never
/// begin.
/// </para>
/// </summary>
public sealed class OrganizationOwnerAuthorityTests : TestBase
{
    [Test]
    public async Task Registering_an_organization_makes_its_owner_able_to_administer_it()
    {
        var (identityId, tenantId) = await RegisterAndConfirmAsync();

        var held = await EffectivePermissionsAsync(identityId, tenantId);

        held.ShouldBe(Permissions.OrganizationOwnerCodes.Order(StringComparer.Ordinal), ignoreOrder: true);
        held.ShouldContain(Permissions.MembersInvite, "the owner could not invite anybody before this");
        held.ShouldContain(Permissions.RolesManage);
        held.ShouldContain(Permissions.MembersManage);
    }

    /// <summary>
    /// The floor C5 names: one identity holding both halves. It is satisfied from the first moment an
    /// Organization exists, which is what stops the very first administrative change from being refused.
    /// </summary>
    [Test]
    public async Task A_new_organization_already_satisfies_the_administrator_floor()
    {
        var (identityId, tenantId) = await RegisterAndConfirmAsync();

        var held = await EffectivePermissionsAsync(identityId, tenantId);

        held.ShouldContain(Permissions.RolesManage);
        held.ShouldContain(Permissions.MembersManage);
    }

    [Test]
    public async Task The_owner_holds_nothing_the_catalogue_does_not_allow_an_organization()
    {
        var (identityId, tenantId) = await RegisterAndConfirmAsync();

        var held = await EffectivePermissionsAsync(identityId, tenantId);

        held.ShouldAllBe(code => Permissions.Catalog
            .Single(definition => definition.Code == code)
            .AllowedTenantTypes.Contains(TenantType.Organization));
        held.ShouldNotContain(Permissions.PlatformAdminsManage);
        held.ShouldNotContain(Permissions.PlatformTenantsManage);
    }

    /// <summary>
    /// The one owner an `Organization` has, named the instant it has an active responsible member. "An
    /// organization always has an owner" is an application invariant, so the moment it starts being true is worth
    /// pinning: a registration that produced no owner would leave nobody able to transfer it.
    /// </summary>
    [Test]
    public async Task Registering_an_organization_names_its_owner()
    {
        var (identityId, tenantId) = await RegisterAndConfirmAsync();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.AsNoTracking().SingleAsync(candidate => candidate.Id == tenantId);
        var membership = await context.TenantMemberships.AsNoTracking().SingleAsync(candidate => candidate.TenantId == tenantId);

        tenant.OwnerMembershipId.ShouldBe(membership.Id);
        membership.IdentityId.ShouldBe(identityId);
    }

    private static async Task<(Guid IdentityId, TenantId TenantId)> RegisterAndConfirmAsync()
    {
        await IdentityHttpHarness.SeedPermissionCatalogAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        (await TestApp.SendAsync(new RegisterOrganizationCommand(email, "Testing1234!", "Owner Org", "30-12345678-9")))
            .IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ConfirmEmailCommand(TestApp.GetRegistrationRawToken()))).IsSuccess.ShouldBeTrue();

        var identity = (await TestApp.ListAsync<ApplicationUser>()).Single(user => user.Email == email);
        var tenant = (await TestApp.ListAsync<Tenant>()).Single(candidate => candidate.Type == TenantType.Organization);
        (await TestApp.ListAsync<TenantMembership>()).Single().Status.ShouldBe(MembershipStatus.Active);
        return (identity.Id, tenant.Id);
    }

    private static async Task<IReadOnlyList<string>> EffectivePermissionsAsync(Guid identityId, TenantId tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IEffectivePermissionReader>()
            .GetEffectivePermissionsAsync(identityId, tenantId, CancellationToken.None);
    }
}
