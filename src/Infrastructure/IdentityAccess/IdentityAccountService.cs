using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    private static readonly ApplicationUser DecoyUser = new();
    private static string? _decoyPasswordHash;

    public async Task<IdentityAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        return user is null ? null : new IdentityAccount(user.Id, user.Email!, user.Status);
    }

    public async Task<IdentityAccount?> FindByIdAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        return user is null ? null : new IdentityAccount(user.Id, user.Email!, user.Status);
    }

    public async Task<IdentityAccount?> ValidateCredentialsAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        await EnsureCurrentAsync(user, cancellationToken);
        var now = timeProvider.GetUtcNow();

        // The state is the whole condition (IA-REQ-054). A lockout is not a state — it is a temporary refusal
        // that expires on its own — so it stays a separate check, and both refuse identically.
        if (user is null || user.Status != IdentityAccountStatus.Active || IsLockedOut(user, now))
        {
            VerifyDecoyPassword(password);
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await RecordFailedAccessAsync(user, now, cancellationToken);
            return null;
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return new IdentityAccount(user.Id, user.Email!, user.Status);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyPasswordAsync(Guid identityId, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        await EnsureCurrentAsync(user, cancellationToken);
        var now = timeProvider.GetUtcNow();

        // Deliberately no state check. Which states may ask this is the caller's decision — a parked account is
        // the only one that does — and repeating it here would answer "no" to the one caller that exists.
        if (user is null || IsLockedOut(user, now))
        {
            VerifyDecoyPassword(password);
            return false;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await RecordFailedAccessAsync(user, now, cancellationToken);
            return false;
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryTransitionAsync(
        Guid identityId,
        IdentityAccountStatus expected,
        IdentityAccountStatus next,
        CancellationToken cancellationToken)
    {
        if (expected == next) throw new ArgumentException("A transition must change the state.", nameof(next));

        var stamp = Guid.NewGuid().ToString();
        var moved = await context.Users
            .Where(candidate => candidate.Id == identityId && candidate.Status == expected)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, next)
                .SetProperty(candidate => candidate.ConcurrencyStamp, stamp), cancellationToken);

        if (moved == 0) return false;

        // The statement went round EF's change tracker, so anything already tracking this row is describing a
        // state that no longer exists. Bring it back onto the committed row rather than letting a later flush
        // write the old one back.
        if (context.ChangeTracker.Entries<ApplicationUser>()
                .FirstOrDefault(entry => entry.Entity.Id == identityId) is { } tracked)
        {
            await tracked.ReloadAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Re-reads the row this check is about to judge.
    /// <para>
    /// A query for a row EF is already tracking answers with the instance it already has, values and all. So a
    /// lookup made earlier in the same request — the identity a sign-in reads to know whose security version to
    /// compare, or the profile a reauthentication reads to know which address to check — leaves behind a user
    /// whose password hash, lockout window and failure count are the ones from that earlier moment. If a password
    /// change commits in between, this check would then accept a password that no longer opens anything, and the
    /// version comparison that follows would compare the new version against itself and see nothing wrong.
    /// </para>
    /// <para>
    /// One statement, and only where it is load-bearing: the credential is judged against the credential as it
    /// stands. An entry carrying unsaved changes is left alone, because reloading would discard them and no
    /// credential check runs in a scope that is mid-write on the same identity (IA-REQ-051).
    /// </para>
    /// </summary>
    private async Task EnsureCurrentAsync(ApplicationUser? user, CancellationToken cancellationToken)
    {
        if (user is null) return;

        var entry = context.Entry(user);
        if (entry.State == EntityState.Unchanged) await entry.ReloadAsync(cancellationToken);
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

    public async Task<IdentityAccountValidationResult> ValidatePasswordAsync(string password, CancellationToken cancellationToken)
    {
        // Only the password validators. The user validators would read the store to reject a duplicate name, and
        // running them here would make this answer depend on whether the address exists — exactly the disclosure
        // this method exists to avoid.
        foreach (var validator in passwordValidators)
        {
            if (!(await validator.ValidateAsync(userManager, DecoyUser, password)).Succeeded) return new IdentityAccountValidationResult(false);
        }

        return new IdentityAccountValidationResult(true);
    }

    public async Task<IdentityAccountCreationResult> CreatePendingAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        var result = await userManager.CreateAsync(user, password);
        return result.Succeeded
            ? new IdentityAccountCreationResult(new IdentityAccount(user.Id, user.Email!, user.Status), false)
            : new IdentityAccountCreationResult(null, true);
    }

    public string HashPassword(string password) => passwordHasher.HashPassword(DecoyUser, password);

    public async Task<IdentityAccountCreationResult> CreatePendingFromHashAsync(string normalizedEmail, string passwordHash, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        user.PasswordHash = passwordHash;

        // The overload without a password skips password validation — the policy was applied when the hash was
        // produced — while still running the user validators, so a normalized address that has been taken since
        // initiation fails here rather than creating a second identity for it.
        var result = await userManager.CreateAsync(user);
        return result.Succeeded
            ? new IdentityAccountCreationResult(new IdentityAccount(user.Id, user.Email!, user.Status), false)
            : new IdentityAccountCreationResult(null, true);
    }

    public async Task ActivateAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString()) ?? throw new InvalidOperationException("identity_not_found");
        if (user.EmailConfirmed && user.Status != IdentityAccountStatus.PendingConfirmation) return;
        user.EmailConfirmed = true;

        // Confirming an address is what moves an account out of `PendingConfirmation`, and only out of that one:
        // an account somebody parked or an operator suspended is not brought back by confirming its address.
        if (user.Status == IdentityAccountStatus.PendingConfirmation) user.Status = IdentityAccountStatus.Active;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("identity_activation_failed");
    }

    /// <summary>
    /// Mirrors <see cref="UserManager{TUser}.AccessFailedAsync"/> with the injected clock instead of the system
    /// clock: reaching the configured number of failures locks the account for the configured duration and starts
    /// a fresh failure window.
    /// <para>
    /// It is one conditional statement rather than a read-modify-write, so the row lock PostgreSQL takes for the
    /// UPDATE serializes competing failures of the same account and re-evaluates the increment against the row
    /// that actually won. No attempt can be dropped by a lost update, which is what makes the configured attempt
    /// exactly the one that locks the account (IA-REQ-019). The response stays the neutral one an unknown account
    /// receives, so neither existence nor lockout is ever revealed (IA-REQ-029).
    /// </para>
    /// <para>
    /// The re-evaluation this relies on is PostgreSQL's READ COMMITTED behaviour, which is the connection default
    /// and is never raised anywhere in this project. Sign-in deliberately validates credentials outside
    /// <c>IApplicationTransaction</c>, so this statement holds its row lock for its own duration only.
    /// </para>
    /// </summary>
    private async Task RecordFailedAccessAsync(ApplicationUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lockout = identityOptions.Value.Lockout;
        var maxFailedAccessAttempts = lockout.MaxFailedAccessAttempts;
        DateTimeOffset? lockoutEnd = now.Add(lockout.DefaultLockoutTimeSpan);
        var concurrencyStamp = Guid.NewGuid().ToString();

        await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.AccessFailedCount, candidate => candidate.AccessFailedCount + 1 >= maxFailedAccessAttempts ? 0 : candidate.AccessFailedCount + 1)
                .SetProperty(candidate => candidate.LockoutEnd, candidate => candidate.AccessFailedCount + 1 >= maxFailedAccessAttempts ? lockoutEnd : candidate.LockoutEnd)
                .SetProperty(candidate => candidate.ConcurrencyStamp, concurrencyStamp), cancellationToken);

        // Bring the tracked entity back onto the committed row so nothing later in this request flushes the
        // pre-update values it still holds.
        await context.Entry(user).ReloadAsync(cancellationToken);
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
        new() { UserName = normalizedEmail, Email = normalizedEmail, EmailConfirmed = false, Status = IdentityAccountStatus.PendingConfirmation };
}
