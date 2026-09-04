using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;

/// <summary>Reissuing is re-inviting, so it carries the same permission as issuing (IA-REQ-017).</summary>
[Authorize(Permissions.MembersInvite, true)]
public sealed record ResendInvitationCommand(TenantId TenantId, Guid InvitationId) : IRequest<Result>;
