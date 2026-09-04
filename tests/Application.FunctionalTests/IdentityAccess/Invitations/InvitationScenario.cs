using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// The organization an invitation is issued from, and the assertions every invitation test shares.
/// <para>
/// <see cref="IdentityHttpHarness.SeedActiveMembershipAsync"/> returns only a tenant id, but an invitation offers
/// roles by id, so this seeds the same graph and hands back the roles too.
/// </para>
/// </summary>
internal static class InvitationScenario
{
    internal sealed record Organization(TenantId TenantId, Guid InviterIdentityId, Guid RoleId, Guid SecondRoleId);

    /// <summary>
    /// An active organization whose inviter holds <paramref name="permissionCodes"/> through a custom role, plus a
    /// second offerable role. The inviter's own role is offerable too, so a test can invite with either.
    /// </summary>
    internal static async Task<Organization> SeedOrganizationAsync(params string[] permissionCodes)
    {
        await IdentityHttpHarness.SeedPermissionCatalogAsync();
        var inviter = await IdentityHttpHarness.SeedConfirmedUserAsync($"inviter-{Guid.NewGuid():N}@example.test", "Testing1234!");

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"invitations-{Guid.NewGuid():N}"));
        tenant.Activate();
        var membership = TenantMembership.CreateResponsible(tenant, inviter);
        membership.Activate(tenant);
        var role = Role.Create(tenant, $"inviter-{Guid.NewGuid():N}");
        var offerable = Role.Create(tenant, $"offerable-{Guid.NewGuid():N}");
        context.AddRange(tenant, membership, role, offerable);
        context.Add(MembershipRole.Create(tenant, membership, role));

        foreach (var code in permissionCodes)
        {
            var permission = await context.Permissions.SingleAsync(candidate => candidate.Code == code);
            context.Add(RolePermission.Create(tenant, role, permission));
        }

        await context.SaveChangesAsync();
        return new Organization(tenant.Id, inviter, role.Id.Value, offerable.Id.Value);
    }

    /// <summary>Puts the inviter behind the request: their identity, their active tenant, and their permission.</summary>
    internal static void ActAs(Organization organization, bool permissionGranted = true)
    {
        TestApp.SetUserId(organization.InviterIdentityId);
        TestApp.SetCurrentTenant(organization.TenantId);
        TestApp.SetApplicationPermissionGranted(permissionGranted);
    }

    /// <summary>A confirmed identity that can sign in and accept, as IA-REQ-005 and IA-REQ-016 require.</summary>
    internal static Task<Guid> SeedConfirmedRecipientAsync(string email) =>
        IdentityHttpHarness.SeedConfirmedUserAsync(email, "Testing1234!");

    internal static async Task<Invitation> SingleInvitationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Invitations.Include(invitation => invitation.Roles).SingleAsync();
    }

    /// <summary>
    /// The evidence an issued invitation must leave behind, restated once: the recipient reaches the aggregate in
    /// canonical form, the delivery intent is written with the business change (IA-REQ-027), and the usable token
    /// is nowhere but the encrypted envelope (IA-REQ-018/029).
    /// </summary>
    internal static async Task AssertDeliveryIntentAsync(string rawToken)
    {
        var outbox = (await TestApp.ListAsync<OutboxMessage>()).Single();
        var secret = (await TestApp.ListAsync<OutboxSecret>()).Single();

        secret.OutboxMessageId.ShouldBe(outbox.Id, "the envelope belongs to the message that will deliver it");
        // A raw token is forbidden in an outbox payload (IA-REQ-018).
        outbox.Payload.ShouldNotContain(rawToken);
        secret.VersionedHash.ShouldNotContain(rawToken);
        secret.Ciphertext.ShouldNotBeNull();
        // The envelope stores ciphertext, never the token.
        secret.Ciphertext!.ShouldNotContain(rawToken);
        secret.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow, "an envelope that never expires is a token that never expires");
    }

    /// <summary>Nothing durable happened: the assertion a denial or a rejected input has to survive.</summary>
    internal static async Task AssertNoInvitationEffectsAsync()
    {
        (await TestApp.CountAsync<Invitation>()).ShouldBe(0);
        (await TestApp.CountAsync<InvitationRole>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0);
    }
}
