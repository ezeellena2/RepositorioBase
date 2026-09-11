using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Application.IdentityAccess.Platform.Invitations;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Infrastructure.IdentityAccess;
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
    internal static async Task<(string Email, string Token)> PendingInvitationAsync(
        bool isOwner = true,
        string? email = null,
        string language = "en")
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
            language,
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

    /// <summary>Closes the window on every confirmation envelope, which is all letting one expire does.</summary>
    internal static async Task ExpireConfirmationEnvelopeAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE outbox_secrets SET \"ExpiresAt\" = NOW() - INTERVAL '1 minute' WHERE \"OutboxMessageId\" IN " +
            "(SELECT \"Id\" FROM outbox_messages WHERE \"Type\" = 'platform.invitation.confirmation.requested')");
    }

    /// <summary>
    /// Marks one confirmation envelope delivered. The functional harness strips every hosted service, so no
    /// dispatcher ever runs and a secret would otherwise stay Pending forever — which would hide the difference
    /// between retiring the pending envelopes and retiring the ones confirmation actually accepts.
    /// </summary>
    internal static async Task MarkConfirmationSecretDeliveredAsync(Guid outboxMessageId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secret = await context.OutboxSecrets.SingleAsync(item => item.OutboxMessageId == outboxMessageId);
        secret.MarkDelivered("confirmation_delivered", DateTimeOffset.UtcNow, "test-receipt");
        await context.SaveChangesAsync();
    }

    /// <summary>Runs the ceremony the host runs at start-up, with the address a test chose.</summary>
    internal static async Task<bool> BootstrapAsync(string? ownerEmail)
    {
        TestApp.SetPlatformBootstrapEmail(ownerEmail);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PermissionCatalogSynchronizer>().SynchronizeAsync(CancellationToken.None);
        var bootstrapper = scope.ServiceProvider.GetRequiredService<BootstrapPlatformOwner>();
        return await bootstrapper.ExecuteAsync(new BootstrapPlatformOwnerCommand(), CancellationToken.None);
    }

    /// <summary>Reads the invitation token out of the envelope the ceremony sealed, as its recipient would.</summary>
    internal static async Task<string> PendingOwnerTokenAsync()
    {
        var invitation = await SingleInvitationAsync();
        return await SealedTokenAsync(invitation.DeliveryMessageId!.Value);
    }

    /// <summary>Marks the message carrying the current token as permanently undeliverable.</summary>
    internal static async Task FailDeliveryAsync()
    {
        var invitation = await SingleInvitationAsync();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlAsync(
            $"""UPDATE outbox_messages SET "Status" = 'Abandoned', "FailureCode" = 'provider_rejected', "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL WHERE "Id" = {invitation.DeliveryMessageId!.Value}""");
    }

    /// <summary>
    /// The owner this ceremony produced, including the recovery codes it was handed. They are kept because the
    /// enrollment stores only hashes: the plaintext exists exactly once, in the answer the ceremony gave, and a
    /// test about spending one has nowhere else to get it.
    /// </summary>
    internal sealed record ActiveOwner(
        Guid IdentityId, TenantId PlatformId, string Token, string SharedKey, IReadOnlyList<string> RecoveryCodes);

    /// <summary>
    /// The whole chain, walked as its recipient would: bootstrap, register, confirm, sign in, enrol, verify,
    /// acknowledge. It is the premise of every test about operating Platform, and it is walked rather than
    /// seeded because seeding an active Platform membership would skip the gates the tests exist to trust.
    /// </summary>
    internal static async Task<ActiveOwner> ActiveOwnerAsync(string ownerEmail = "platform-owner@example.test")
    {
        await BootstrapAsync(ownerEmail);
        var token = await PendingOwnerTokenAsync();
        RunAnonymously();
        await TestApp.SendAsync(new RegisterPlatformInviteeCommand(token, ValidPassword));
        var confirmation = await SealedTokenAsync(
            (await MessagesAsync()).Last(message => message.Type == "platform.invitation.confirmation.requested").Id);
        await TestApp.SendAsync(new ConfirmPlatformInviteeCommand(confirmation));

        var normalized = ownerEmail.ToUpperInvariant();
        Guid identityId;
        TenantId platformId;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            identityId = (await context.Set<CleanArchitecture.Infrastructure.Identity.ApplicationUser>()
                .SingleAsync(user => user.NormalizedEmail == normalized)).Id;
            platformId = (await context.Tenants.SingleAsync(tenant => tenant.Type == TenantType.Platform)).Id;
        }

        RunAs(identityId);
        var enrollment = await TestApp.SendAsync(new BeginPlatformMfaEnrollmentCommand(token));
        await TestApp.SendAsync(new VerifyPlatformMfaEnrollmentCommand(token, TotpCode(enrollment.Value!.SharedKey)));
        (await TestApp.SendAsync(new AcknowledgePlatformRecoveryCodesCommand(token))).IsSuccess.ShouldBeTrue();

        // From here the caller acts as a Platform member: the tenant comes from the session, never from input.
        TestApp.SetCurrentTenant(platformId);
        TestApp.SetApplicationPermissionGranted(true);
        return new ActiveOwner(identityId, platformId, token, enrollment.Value.SharedKey, enrollment.Value.RecoveryCodes);
    }

    /// <summary>What a real authenticator would show for this key now. Computed here, not asked of the code.</summary>
    internal static string TotpCode(string sharedKey)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var character in sharedKey.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(char.ToUpperInvariant(character));
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
            bitsLeft -= 8;
        }

        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        Span<byte> mac = stackalloc byte[20];
        System.Security.Cryptography.HMACSHA1.HashData([.. bytes], counter, mac);
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
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
