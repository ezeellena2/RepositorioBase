using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;

/// <summary>
/// The tenant is compared against the validated session's active tenant; it never establishes context.
/// </summary>
[Authorize(Permissions.MembersInvite, true)]
public sealed record InviteMemberCommand(TenantId TenantId, string Email, IReadOnlyList<Guid> RoleIds)
    : IRequest<Result<IssuedInvitation>>;

/// <summary>
/// Deliberately carries no token. The usable credential is minted for the recipient and reaches them through the
/// encrypted outbox envelope; handing it back to whoever issued the invitation would let anyone holding
/// members.invite register an account for an address they do not control (IA-REQ-015/018).
/// </summary>
public sealed record IssuedInvitation(Guid InvitationId, DateTimeOffset ExpiresAt);
