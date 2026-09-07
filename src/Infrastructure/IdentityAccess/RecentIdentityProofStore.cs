using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Where a recent identity proof lives and how it is spent (IA-REQ-051).
/// <para>
/// Consumption is a conditional update rather than a read followed by a write: two requests racing the same proof
/// must not both believe they spent it, and the row lock is what decides which one did.
/// </para>
/// </summary>
public sealed class RecentIdentityProofStore(ApplicationDbContext context, TimeProvider timeProvider) : IRecentIdentityProofStore
{
    /// <summary>How long a proof stays spendable. A **product default**.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task IssueAsync(
        Guid identityId,
        UserSessionId sessionId,
        string action,
        RecentIdentityProofMethod method,
        DateTimeOffset? notAfter,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // At most one live proof per identity, session and action — the partial unique index says so, and a fresh
        // proof replaces rather than stacks, so proving twice does not leave a spare to spend later.
        await context.RecentIdentityProofs
            .Where(proof => proof.IdentityId == identityId && proof.SessionId == sessionId && proof.Action == action && proof.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(proof => proof.ConsumedAt, now)
                .SetProperty(proof => proof.ConsumedReason, "superseded")
                .SetProperty(proof => proof.Version, proof => proof.Version + 1), cancellationToken);

        // Whichever runs out first. A caller that hands over a deadline is saying this proof rests on something
        // it does not own, and a proof that outlived what it rests on would prove nothing.
        var lifetime = notAfter is { } deadline && deadline - now < Lifetime ? deadline - now : Lifetime;
        if (lifetime <= TimeSpan.Zero)
        {
            // Nothing left to issue. The supersession above still stands, so an older proof cannot be spent in
            // place of the one this attempt failed to earn — which is the fail-closed direction.
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var version = await CurrentVersionAsync(identityId, cancellationToken);
        context.RecentIdentityProofs.Add(RecentIdentityProof.Issue(identityId, sessionId, action, method, version, now, lifetime));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(Guid identityId, UserSessionId sessionId, string action, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var version = await CurrentVersionAsync(identityId, cancellationToken);

        // Everything the contract requires, evaluated by PostgreSQL under the row lock in one statement: the right
        // identity, the right session, the right action, unspent, unexpired, and a security version that nothing
        // has moved since the proof was issued.
        var affected = await context.RecentIdentityProofs
            .Where(proof => proof.IdentityId == identityId
                && proof.SessionId == sessionId
                && proof.Action == action
                && proof.ConsumedAt == null
                && proof.ExpiresAt > now
                && proof.SecurityVersion == version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(proof => proof.ConsumedAt, now)
                .SetProperty(proof => proof.ConsumedReason, "spent")
                .SetProperty(proof => proof.Version, proof => proof.Version + 1), cancellationToken);

        return affected == 1;
    }

    public async Task<long> CurrentVersionAsync(Guid identityId, CancellationToken cancellationToken) =>
        await context.IdentitySecurityStates
            .AsNoTracking()
            .Where(state => state.IdentityId == identityId)
            .Select(state => state.SecurityVersion)
            .SingleOrDefaultAsync(cancellationToken);

    public Task AdvanceVersionAsync(Guid identityId, CancellationToken cancellationToken) =>
        AdvanceAsync(identityId, stampPassword: false, cancellationToken);

    public Task RecordPasswordChangeAsync(Guid identityId, CancellationToken cancellationToken) =>
        AdvanceAsync(identityId, stampPassword: true, cancellationToken);

    public async Task<DateTimeOffset?> PasswordUpdatedAtAsync(Guid identityId, CancellationToken cancellationToken) =>
        await context.IdentitySecurityStates
            .AsNoTracking()
            .Where(state => state.IdentityId == identityId)
            .Select(state => state.PasswordUpdatedAt)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task AdvanceAsync(Guid identityId, bool stampPassword, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Two statements rather than one branch inside the setter list, because `ExecuteUpdate` cannot express a
        // conditional column and writing `PasswordUpdatedAt` unconditionally is the exact bug this avoids.
        var affected = stampPassword
            ? await context.IdentitySecurityStates
                .Where(state => state.IdentityId == identityId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.SecurityVersion, state => state.SecurityVersion + 1)
                    .SetProperty(state => state.UpdatedAt, now)
                    .SetProperty(state => state.PasswordUpdatedAt, now)
                    .SetProperty(state => state.Version, state => state.Version + 1), cancellationToken)
            : await context.IdentitySecurityStates
                .Where(state => state.IdentityId == identityId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.SecurityVersion, state => state.SecurityVersion + 1)
                    .SetProperty(state => state.UpdatedAt, now)
                    .SetProperty(state => state.Version, state => state.Version + 1), cancellationToken);

        // An identity with no row is at version zero, so the first advance creates it at one. Nothing is
        // backfilled: a row that never existed is indistinguishable from one that was never advanced.
        if (affected == 0)
        {
            var state = IdentitySecurityState.Start(identityId, now);
            if (stampPassword) state.AdvanceForPasswordChange(now); else state.Advance(now);
            context.IdentitySecurityStates.Add(state);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// What device this request came from, in the only vocabulary the system keeps (IA-REQ-049).
/// <para>
/// The raw `User-Agent` is read and thrown away. What survives is one token from a closed set, which is enough for
/// a person to recognize their own phone and not enough to identify a browser build.
/// </para>
/// </summary>
public sealed class DeviceLabelResolver(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : Application.IdentityAccess.Sessions.IDeviceLabel
{
    public string Current
    {
        get
        {
            var agent = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(agent)) return SessionDeviceLabel.Other;

            // Order matters: an iPad reports "Macintosh" too, and Android reports "Linux".
            if (agent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPad";
            if (agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "iPhone";
            if (agent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
            if (agent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
            if (agent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || agent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "macOS";
            if (agent.Contains("Linux", StringComparison.OrdinalIgnoreCase) || agent.Contains("X11", StringComparison.OrdinalIgnoreCase)) return "Linux";
            return SessionDeviceLabel.Other;
        }
    }
}
