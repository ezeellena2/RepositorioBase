namespace CleanArchitecture.Domain.IdentityAccess.Identities;

/// <summary>
/// What an identity account is allowed to be, and the only source of the "active identity" condition
/// IA-REQ-020 states (IA-REQ-054).
/// <para>
/// The set is finite and closed on purpose. "Can this person sign in?" used to be assembled at each call site out
/// of a confirmed address and an unexpired lockout, which meant every new way to stop an account had to be
/// remembered by every reader. Here there is one answer — <see cref="Active"/> — and everything else is a state
/// with a name, an actor and a way back, or a terminal state that says it has none.
/// </para>
/// </summary>
public enum IdentityAccountStatus
{
    /// <summary>Created, address not confirmed yet. Reached by registration and by invitation registration.</summary>
    PendingConfirmation,

    /// <summary>Confirmed and usable. The only state that may sign in.</summary>
    Active,

    /// <summary>
    /// The person parked their own account. Reached from <see cref="Active"/> by the person, and left the same
    /// way: a public single-use ticket mailed to the address, spent together with a current password.
    /// </summary>
    SelfDeactivated,

    /// <summary>
    /// A Platform operator stopped the account, under a closed reason set recorded only in audit. No self-service
    /// route reaches it, and recovering a credential does not touch it.
    /// </summary>
    AdministrativelySuspended,

    /// <summary>
    /// Erasure executed; a terminal tombstone. It names no actor, no proof and no endpoint, because there is no
    /// way back from it — which is what a tombstone means.
    /// </summary>
    Closed
}
