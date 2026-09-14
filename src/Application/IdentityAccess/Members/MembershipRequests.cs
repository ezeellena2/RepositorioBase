using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Members;

/// <summary>
/// Member administration inside one `Organization` (IA-REQ-053).
/// <para>
/// Every request names the tenant it is for and every handler compares that name against the session's active
/// tenant rather than trusting it: the pipeline authorizes against the session, so honouring a different tenant
/// would be authorizing against one and acting on another.
/// </para>
/// </summary>
[Authorize(Permissions.MembersRead, true)]
public sealed record ListMembersQuery(TenantId TenantId, PaginationQuery Pagination) : IRequest<Result<PaginatedList<MemberView>>>;

[Authorize(Permissions.MembersRead, true)]
public sealed record ListTenantInvitationsQuery(TenantId TenantId, PaginationQuery Pagination) : IRequest<Result<PaginatedList<InvitationSummaryView>>>;

/// <summary>
/// Handing somebody a role is handing them everything in it, so this is where the grant ceiling applies to the
/// whole set rather than to what changed — and it needs a live proof of its own (amendment D2).
/// </summary>
[Authorize(Permissions.MembersManage, true)]
public sealed record UpdateMemberRolesCommand(TenantId TenantId, Guid MembershipId, IReadOnlyList<Guid>? RoleIds, string? Version)
    : IRequest<Result<MemberView>>, ISensitiveRequest;

/// <summary>
/// Suspending, reactivating and revoking. They share a request because they differ only in the state asked for,
/// and keeping them apart would mean three copies of the floor check and the owner rule.
/// </summary>
public enum MemberStatusChange
{
    Suspend,
    Reactivate,
    Revoke
}

[Authorize(Permissions.MembersManage, true)]
public sealed record ChangeMemberStatusCommand(TenantId TenantId, Guid MembershipId, MemberStatusChange Change, string? Version)
    : IRequest<Result>, ISensitiveRequest;

/// <summary>
/// Giving the organization away. The permission is necessary and never sufficient — the actor must also be the
/// current owner — and it is separate from `tenant.manage` because managing a tenant and giving it away are
/// different powers (IA-REQ-053).
/// </summary>
[Authorize(Permissions.TenantOwnershipTransfer, true)]
public sealed record TransferOwnershipCommand(TenantId TenantId, Guid ToMembershipId, string? Version)
    : IRequest<Result>, ISensitiveRequest;
