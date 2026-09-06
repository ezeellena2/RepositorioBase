using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.People;

/// <summary>The premises a person's own context needs, seeded directly rather than driven through the product.</summary>
internal static class PersonalScenario
{
    internal const string ValidPassword = "Testing1234!";

    internal static Task<Guid> SeedConfirmedIdentityAsync(string email) =>
        IdentityHttpHarness.SeedConfirmedUserAsync(email, ValidPassword);

    /// <summary>An Organization this identity already belongs to, so adding Personal is provably additive.</summary>
    internal static async Task<TenantId> SeedOrganizationForAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = Tenant.CreateOrganization(TenantSlug.From($"org-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        membership.Activate(tenant);
        context.Tenants.Add(tenant);
        context.TenantMemberships.Add(membership);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>An Organization whose membership actually grants something, so a context switch has permissions to compare.</summary>
    internal static async Task<TenantId> SeedOrganizationWithPermissionsAsync(Guid identityId, params string[] permissionCodes)
    {
        await IdentityHttpHarness.SeedPermissionCatalogAsync();
        return await IdentityHttpHarness.SeedActiveMembershipAsync(identityId, permissionCodes);
    }

    /// <summary>
    /// Acts as an identity that also holds a persisted session, which anything reading or selecting a context
    /// needs: the session is the durable record, and a session identifier with no row behind it is not one.
    /// </summary>
    internal static async Task RunWithSessionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = UserSession.Create(identityId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();

        TestApp.SetUserId(identityId);
        TestApp.SetSessionId(session.Id.Value);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
    }

    internal static void RunAs(Guid identityId)
    {
        TestApp.SetUserId(identityId);
        TestApp.SetSessionId(Guid.NewGuid());
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
    }
}
