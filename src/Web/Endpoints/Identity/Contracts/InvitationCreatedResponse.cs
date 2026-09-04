namespace CleanArchitecture.Web.IdentityEndpoints.Contracts;

/// <summary>
/// The issue response deliberately omits the token. It is a credential minted for the recipient and delivered
/// through the encrypted outbox envelope; returning it here would let anyone holding members.invite create an
/// account for an address they do not control (IA-REQ-015/018).
/// </summary>
public sealed record InvitationCreatedResponse(Guid InvitationId, DateTimeOffset ExpiresAt);
