namespace CleanArchitecture.Domain.IdentityAccess.Invitations;

/// <summary>
/// The states an invitation is durably in. Expiry is deliberately absent: it is derived from
/// <see cref="Invitation.ExpiresAt"/>, so no background sweeper is needed to keep the row honest and a lapsed
/// invitation can still be reissued in place.
/// </summary>
public enum InvitationStatus
{
    Pending,
    Accepted,
    Cancelled
}
