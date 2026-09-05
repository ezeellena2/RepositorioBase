namespace CleanArchitecture.Domain.IdentityAccess.Platform;

/// <summary>
/// The states a Platform invitation is durably in. As with an organization invitation, expiry is derived rather
/// than stored, so a lapsed offer stays reissuable in place instead of needing a sweeper to retire it.
/// </summary>
public enum PlatformAdminInvitationStatus
{
    Pending,
    Accepted,
    Cancelled
}
