using CleanArchitecture.Domain.IdentityAccess.Identities;

namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// What an account's lifecycle looks like from outside ASP.NET Identity's schema: where it is, and — when an
/// operator stopped it — where it was before they did.
/// </summary>
public sealed record IdentityLifecycleState(Guid IdentityId, IdentityAccountStatus Status, IdentityAccountStatus? StatusBeforeSuspension);

/// <summary>
/// The administrative half of the identity lifecycle (IA-REQ-054). It is a separate port from
/// <c>IIdentityAccountService</c> because it exists for exactly one caller — the Platform operator routes — and
/// widening the general identity boundary with "suspend anybody" would make that power look ordinary.
/// <para>
/// Nothing here decides policy. Which state may be suspended, and which state a reactivation lands in, are the
/// handler's decisions; this only applies them conditionally, so two operators cannot both believe they were the
/// one who moved the account.
/// </para>
/// </summary>
public interface IIdentityLifecycleStore
{
    Task<IdentityLifecycleState?> FindAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the account to <see cref="IdentityAccountStatus.AdministrativelySuspended"/> if and only if it is
    /// still in <paramref name="expected"/>, remembering that state so lifting the suspension can put the account
    /// back where it was rather than somewhere convenient.
    /// </summary>
    Task<bool> TrySuspendAsync(Guid identityId, IdentityAccountStatus expected, CancellationToken cancellationToken);

    /// <summary>
    /// Lifts the suspension into <paramref name="restored"/> if and only if the account is still suspended, and
    /// forgets what it was before — because a state that has been restored is no longer a state being remembered.
    /// </summary>
    Task<bool> TryLiftSuspensionAsync(Guid identityId, IdentityAccountStatus restored, CancellationToken cancellationToken);
}
