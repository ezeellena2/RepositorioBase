using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// The guard behind amendment D1: what an `Organization`'s system `Owner` role holds is a decision somebody made,
/// never a side effect of shipping a feature.
/// <para>
/// C5's grant ceiling is what makes this load-bearing. An actor may grant only what it effectively holds, so a
/// code the owner does not hold is a code nobody in any organization can ever grant — and, the other way round, a
/// code silently added to the owner widens every existing owner's authority the moment it deploys. Neither may
/// happen by accident, so the exact set is written out here: adding a permission fails this test until the person
/// adding it says which side it falls on.
/// </para>
/// </summary>
public sealed class OrganizationOwnerCatalogTests
{
    /// <summary>
    /// The set as decided on 2026-09-06, when C5 was accepted. A diff on this list is the record of an owner's
    /// authority changing, which is exactly what D1 asks for.
    /// </summary>
    private static readonly string[] Decided =
    [
        Permissions.MembersInvite,
        Permissions.MembersManage,
        Permissions.MembersRead,
        Permissions.RolesManage,
        Permissions.RolesRead,
        Permissions.TenantManage,
        Permissions.TenantOwnershipTransfer,
        Permissions.TenantRead
    ];

    [Test]
    public void The_owner_holds_exactly_the_codes_somebody_decided_it_holds()
    {
        Permissions.OrganizationOwnerCodes.ShouldBe(Decided, ignoreOrder: true,
            customMessage: "adding a permission is not a reason to widen every existing owner's authority, and " +
                           "withholding one from the owner means nobody can ever grant it. Decide, then update this list.");
    }

    [Test]
    public void Every_organization_permission_has_an_owner_answer_that_is_not_the_platform_one()
    {
        foreach (var definition in Permissions.Catalog.Where(entry => entry.AllowedTenantTypes.Contains(TenantType.Organization)))
        {
            definition.OrganizationOwner.ShouldNotBe(OrganizationOwner.NotApplicable,
                $"{definition.Code} is allowed for an Organization, so whether its owner holds it is a real question.");
        }
    }

    [Test]
    public void Nothing_an_organization_may_not_hold_is_given_to_its_owner()
    {
        foreach (var definition in Permissions.Catalog.Where(entry => !entry.AllowedTenantTypes.Contains(TenantType.Organization)))
        {
            definition.OrganizationOwner.ShouldBe(OrganizationOwner.NotApplicable,
                $"{definition.Code} is not allowed for an Organization at all.");
        }
    }

    /// <summary>
    /// The floor C5 refuses to commit without. If the owner did not hold both halves, the very first
    /// administrative change in a brand-new organization would answer `last_administrator_required`.
    /// </summary>
    [Test]
    public void The_owner_satisfies_the_effective_administrator_floor_on_its_own()
    {
        Permissions.OrganizationOwnerCodes.ShouldContain(Permissions.RolesManage);
        Permissions.OrganizationOwnerCodes.ShouldContain(Permissions.MembersManage);
    }
}
