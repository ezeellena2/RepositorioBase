using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The state a Platform journey starts from, seeded directly. These rows are the premise of a test, not the thing
/// under test: a test about onboarding should fail when onboarding breaks, not when the ceremony that created the
/// tenant does. Everything a test actually asserts still goes through the request pipeline.
/// </summary>
internal static class PlatformScenario
{
    internal const string ValidPassword = "Testing1234!";

    internal static async Task<Tenant> ActivePlatformAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await context.Tenants.FirstOrDefaultAsync(tenant => tenant.Type == TenantType.Platform);
        if (existing is not null) return existing;

        var platform = Tenant.CreatePlatform();
        platform.Activate();
        context.Add(platform);
        await context.SaveChangesAsync();
        return platform;
    }

    /// <summary>
    /// A pending Platform invitation whose token the test chose. The row stores only the hash, produced by the
    /// same type the application uses rather than by a copy of its rule that could drift from it.
    /// </summary>
    internal static async Task<(string Email, string Token)> PendingInvitationAsync(bool isOwner = true, string? email = null)
    {
        var platform = await ActivePlatformAsync();
        var recipient = email ?? $"platform-{Guid.NewGuid():N}@example.test";
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        context.Add(PlatformAdminInvitation.Issue(
            platform,
            recipient,
            VersionedTokenHash.Of(token),
            isOwner,
            now.AddMinutes(-1),
            now.AddDays(7)));
        await context.SaveChangesAsync();
        return (PlatformAdminInvitation.Canonicalize(recipient), token);
    }

    internal static async Task<PlatformAdminInvitation> SingleInvitationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.PlatformAdminInvitations.SingleAsync();
    }

    internal static async Task<List<OutboxMessage>> MessagesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboxMessages.OrderBy(message => message.CreatedAt).ToListAsync();
    }

    /// <summary>Reads back the token an outbox envelope is carrying, the way the dispatcher would.</summary>
    internal static async Task<string> SealedTokenAsync(Guid outboxMessageId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader>();
        return (await reader.ReadAsync(outboxMessageId, CancellationToken.None))
            ?? throw new InvalidOperationException($"Outbox message {outboxMessageId} carries no readable envelope.");
    }

    /// <summary>
    /// The system roles the bootstrap ceremony creates. Activation attaches an existing one and never invents
    /// it, so a test about activation has to seed them as its premise.
    /// </summary>
    internal static async Task SeedPlatformRolesAsync()
    {
        var platform = await ActivePlatformAsync();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync(item => item.Id == platform.Id);
        foreach (var name in new[] { "Owner", "Administrator" })
        {
            var normalized = name.ToUpperInvariant();
            if (await context.TenantRoles.AnyAsync(role => role.TenantId == tenant.Id && role.NormalizedName == normalized)) continue;
            context.TenantRoles.Add(CleanArchitecture.Domain.IdentityAccess.Authorization.Role.CreateSystem(tenant, name));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Puts a seeded identity and a session behind the request, which is what the MFA gates read.</summary>
    internal static void RunAs(Guid identityId)
    {
        TestApp.SetUserId(identityId);
        TestApp.SetSessionId(Guid.NewGuid());
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(true);
    }

    /// <summary>Moves the invitation window into the past, which is the only thing waiting out an expiry does.</summary>
    internal static async Task ExpireInvitationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"PlatformAdminInvitations\" SET \"CreatedAt\" = NOW() - INTERVAL '8 days', \"ExpiresAt\" = NOW() - INTERVAL '1 day' WHERE \"Status\" = 'Pending'");
    }

    internal static void RunAnonymously()
    {
        TestApp.SetUserId(null);
        TestApp.SetCurrentTenant(null);
        TestApp.SetApplicationPermissionGranted(false);
    }
}

/// <summary>
/// Asks ASP.NET Identity itself whether a password is the account's, rather than comparing hashes by hand. It is
/// what proves the submitted password became the real credential and that no default was generated for it.
/// </summary>
internal static class PlatformIdentityProbe
{
    internal static async Task<bool> PasswordMatchesAsync(CleanArchitecture.Infrastructure.Identity.ApplicationUser user, string password)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<CleanArchitecture.Infrastructure.Identity.ApplicationUser>>();
        var stored = await users.FindByIdAsync(user.Id.ToString());
        return stored is not null && await users.CheckPasswordAsync(stored, password);
    }
}
