using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// The administrative lifecycle transitions, each one conditional statement (IA-REQ-054).
/// <para>
/// Both writes are guarded on the state the caller expected, so the first one commits and the second finds its
/// own precondition gone. Both rotate the row's <c>ConcurrencyStamp</c>, so a copy another request is still
/// holding is stale by the time it tries to save.
/// </para>
/// </summary>
public sealed class IdentityLifecycleStore(ApplicationDbContext context) : IIdentityLifecycleStore
{
    public async Task<IdentityLifecycleState?> FindAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == identityId, cancellationToken);
        return user is null ? null : new IdentityLifecycleState(user.Id, user.Status, user.StatusBeforeSuspension);
    }

    public Task<bool> TrySuspendAsync(Guid identityId, IdentityAccountStatus expected, CancellationToken cancellationToken) =>
        ApplyAsync(identityId, expected, IdentityAccountStatus.AdministrativelySuspended, expected, cancellationToken);

    public Task<bool> TryLiftSuspensionAsync(Guid identityId, IdentityAccountStatus restored, CancellationToken cancellationToken) =>
        ApplyAsync(identityId, IdentityAccountStatus.AdministrativelySuspended, restored, null, cancellationToken);

    private async Task<bool> ApplyAsync(
        Guid identityId,
        IdentityAccountStatus expected,
        IdentityAccountStatus next,
        IdentityAccountStatus? remembered,
        CancellationToken cancellationToken)
    {
        var stamp = Guid.NewGuid().ToString();
        var moved = await context.Users
            .Where(candidate => candidate.Id == identityId && candidate.Status == expected)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, next)
                .SetProperty(candidate => candidate.StatusBeforeSuspension, remembered)
                .SetProperty(candidate => candidate.ConcurrencyStamp, stamp), cancellationToken);

        if (moved == 0) return false;

        // The statement went round EF's change tracker, so anything already tracking this row is describing a
        // state that no longer exists.
        if (context.ChangeTracker.Entries<ApplicationUser>()
                .FirstOrDefault(entry => entry.Entity.Id == identityId) is { } tracked)
        {
            await tracked.ReloadAsync(cancellationToken);
        }

        return true;
    }
}
