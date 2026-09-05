namespace CleanArchitecture.Web.PlatformEndpoints.Contracts;

/// <summary>
/// Both halves of the onboarding proof: which Platform offer is being answered, and that its recipient's address
/// received the confirmation.
/// </summary>
public sealed record PlatformInvitationConfirmationRequest(string Token, string ConfirmationToken);
