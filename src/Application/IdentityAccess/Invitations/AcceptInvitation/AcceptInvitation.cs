using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;

/// <summary>
/// An invitee holds no membership until acceptance succeeds, so this cannot require a tenant — but it is not
/// public either: IA-REQ-016 requires an authenticated, confirmed identity whose email matches the recipient,
/// and only an authorized request produces an audited denial.
/// </summary>
[Authorize(Permissions.IdentityInvitationsAccept, false)]
public sealed record AcceptInvitationCommand(string Token) : IRequest<Result<AcceptedInvitation>>, ISensitiveRequest;

public sealed record AcceptedInvitation(Guid TenantId, Guid MembershipId);
