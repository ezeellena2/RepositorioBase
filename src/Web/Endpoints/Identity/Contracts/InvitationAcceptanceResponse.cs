namespace CleanArchitecture.Web.IdentityEndpoints.Contracts;

public sealed record InvitationAcceptanceResponse(Guid TenantId, Guid MembershipId);
