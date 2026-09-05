namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// What became of the message carrying this invitation's token.
/// <para>
/// It is modelled because bootstrap recovery is allowed to rotate only an invitation that expired or whose
/// delivery permanently failed (IA-REQ-040). Without it, recovery would have to either rotate any pending
/// invitation — which would let a caller invalidate a token that is still on its way — or infer the answer from
/// the outbox on every request, which is a second copy of a decision the aggregate should be able to state.
/// </para>
/// </summary>
public enum PlatformAdminInvitationDelivery
{
    Pending,
    Delivered,
    PermanentlyFailed
}
