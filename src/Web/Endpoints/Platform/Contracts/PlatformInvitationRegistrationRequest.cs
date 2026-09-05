namespace CleanArchitecture.Web.PlatformEndpoints.Contracts;

/// <summary>
/// What a Platform invitation's recipient submits. The password is used only when the matching identity is
/// missing, and it is the recipient's own choice: the system never generates or stores a default (IA-REQ-041).
/// There is deliberately no email field — the recipient is whoever the invitation was addressed to.
/// </summary>
public sealed record PlatformInvitationRegistrationRequest(string Token, string Password);
