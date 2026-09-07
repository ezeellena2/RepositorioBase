using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

/// <summary>
/// The per-tenant lock that orders "what a role confers" against "what an offer confers" (IA-REQ-047).
/// <para>
/// The two are decided by reading each other's rows, and a row that is written but not committed is invisible.
/// So an offer validated against a role, and a widening of that same role, can each read a world in which the
/// other has not happened — and both commit. Nothing in the schema catches it: the invitation is well formed and
/// the role is well formed; it is only their combination, at commit time, that grants a permission the inviter
/// never held.
/// </para>
/// <para>
/// The two sides take this lock differently on purpose, and that asymmetry is what keeps them from deadlocking.
/// A widening takes it <em>first</em>, before it has written anything, so it can afford to wait. An offer takes
/// it <em>last</em>, after its rows exist, so it must never wait — it already holds rows a widening may be
/// blocked on. Whoever cannot take it re-reads the world and refuses.
/// </para>
/// </summary>
public interface IRoleAuthorityLock
{
    /// <summary>
    /// Taken first by a transaction that changes what a role confers, before it holds anything. It waits.
    /// </summary>
    Task AcquireAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Taken last by a transaction that establishes an offer, after its rows are written. It never waits, and
    /// <see langword="false"/> means a change to this tenant's roles is in flight — so the offer this transaction
    /// was about to commit can no longer be trusted to be the offer that was authorized.
    /// </summary>
    Task<bool> TryAcquireAsync(TenantId tenantId, CancellationToken cancellationToken);
}
