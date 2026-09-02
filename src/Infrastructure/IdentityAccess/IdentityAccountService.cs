using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class IdentityAccountService(
    UserManager<ApplicationUser> userManager,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<IdentityOptions> identityOptions,
    ApplicationDbContext context,
    TimeProvider timeProvider) : IIdentityAccountService
{
    /// <summary>
    /// A precomputed hash keeps unknown, unconfirmed and locked accounts on the same password-hashing cost as a real
    /// check, so the dominant cost of a sign-in does not reveal whether an account exists or its state (IA-REQ-019/029).
    /// Persisting a failed attempt still adds one database write for confirmed accounts.
    /// </summary>
    private const int FailedAccessPersistenceAttempts = 3;

    private static readonly ApplicationUser DecoyUser = new();
    private static string? _decoyPasswordHash;

    public async Task<IdentityAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        return user is null ? null : new IdentityAccount(user.Id, user.Email!, user.EmailConfirmed);
    }

    public async Task<IdentityAccount?> FindByIdAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        return user is null ? null : new IdentityAccount(user.Id, user.Email!, user.EmailConfirmed);
    }

    public async Task<IdentityAccount?> ValidateCredentialsAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        var now = timeProvider.GetUtcNow();
        if (user is null || !user.EmailConfirmed || IsLockedOut(user, now))
        {
            VerifyDecoyPassword(password);
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await RecordFailedAccessAsync(user, now);
            return null;
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return new IdentityAccount(user.Id, user.Email!, true);
    }

    public async Task<IdentityAccountValidationResult> ValidatePendingRegistrationAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        foreach (var validator in userValidators)
        {
            if (!(await validator.ValidateAsync(userManager, user)).Succeeded) return new IdentityAccountValidationResult(false);
        }

        foreach (var validator in passwordValidators)
        {
            if (!(await validator.ValidateAsync(userManager, user, password)).Succeeded) return new IdentityAccountValidationResult(false);
        }

        return new IdentityAccountValidationResult(true);
    }

    public async Task<IdentityAccountCreationResult> CreatePendingAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        var result = await userManager.CreateAsync(user, password);
        return result.Succeeded
            ? new IdentityAccountCreationResult(new IdentityAccount(user.Id, user.Email!, false), false)
            : new IdentityAccountCreationResult(null, true);
    }

    public async Task ActivateAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString()) ?? throw new InvalidOperationException("identity_not_found");
        if (user.EmailConfirmed) return;
        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("identity_activation_failed");
    }

    /// <summary>
    /// Mirrors <see cref="UserManager{TUser}.AccessFailedAsync"/> with the injected clock instead of the system
    /// clock: reaching the configured number of failures locks the account for the configured duration and starts
    /// a fresh failure window. The count and lockout end are persisted through the <see cref="UserManager{TUser}"/>.
    /// When a parallel failed attempt wins the concurrency check, the committed row is reloaded and the attempt is
    /// re-applied. Losing every retry is never surfaced: the response must stay identical to the one an unknown
    /// account receives, or its status alone would reveal that the account exists (IA-REQ-019/029). The competing
    /// writers that won have already counted their own attempts, so the lockout still advances.
    /// </summary>
    private async Task RecordFailedAccessAsync(ApplicationUser user, DateTimeOffset now)
    {
        for (var attempt = 0; attempt < FailedAccessPersistenceAttempts; attempt++)
        {
            if (attempt > 0) await context.Entry(user).ReloadAsync();
            ApplyFailedAccess(user, now);
            if ((await userManager.UpdateAsync(user)).Succeeded) return;
        }

        // Leave the tracked entity on the committed values so nothing else in this request tries to flush it.
        await context.Entry(user).ReloadAsync();
    }

    private void ApplyFailedAccess(ApplicationUser user, DateTimeOffset now)
    {
        var lockout = identityOptions.Value.Lockout;
        user.AccessFailedCount++;
        if (user.AccessFailedCount >= lockout.MaxFailedAccessAttempts)
        {
            user.LockoutEnd = now.Add(lockout.DefaultLockoutTimeSpan);
            user.AccessFailedCount = 0;
        }
    }

    private void VerifyDecoyPassword(string password)
    {
        // The hash is computed once per process by the configured hasher, so the decoy costs exactly one real
        // verification. The result is irrelevant by design; the caller has already decided to reject.
        var decoyHash = _decoyPasswordHash ??= passwordHasher.HashPassword(DecoyUser, Guid.NewGuid().ToString("N"));
        passwordHasher.VerifyHashedPassword(DecoyUser, decoyHash, password);
    }

    /// <summary>Same boundary as the cookie validation: a lockout ends exactly at its end instant.</summary>
    private static bool IsLockedOut(ApplicationUser user, DateTimeOffset now) =>
        user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > now;

    private static ApplicationUser NewPendingUser(string normalizedEmail) =>
        new() { UserName = normalizedEmail, Email = normalizedEmail, EmailConfirmed = false };
}
