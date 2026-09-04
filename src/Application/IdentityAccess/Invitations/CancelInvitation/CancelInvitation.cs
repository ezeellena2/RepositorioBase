using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;

/// <summary>Withdrawing an offer administers membership rather than extending one.</summary>
[Authorize(Permissions.MembersManage, true)]
public sealed record CancelInvitationCommand(TenantId TenantId, Guid InvitationId) : IRequest<Result>;
