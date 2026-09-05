namespace CleanArchitecture.Web.PlatformEndpoints.Contracts;

/// <summary>
/// The confirmation token the address received. It is the whole proof: the envelope sealed with it already names
/// the invitation and the identity, so there is nothing for a second token to pin down.
/// </summary>
public sealed record PlatformInvitationConfirmationRequest(string ConfirmationToken);
