using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Members;

/// <summary>
/// One member as an administrator sees them (amendment D4). It carries another person's display name and
/// normalized email, which no `Organization`-facing route returned before C5 — recorded because that is the shape
/// the real-personal-data gate inherits, and it is why this needs `members.read` rather than being public.
/// </summary>
public sealed record MemberView(
    Guid MembershipId,
    Guid IdentityId,
    string DisplayName,
    string NormalizedEmail,
    string Status,
    IReadOnlyList<Guid> RoleIds,
    bool IsOwner,
    string Version);

/// <summary>
/// A standing offer, as the administrator who might withdraw it sees it. No token, no envelope, nothing that
/// could be used to accept — only what the offer is and who it is for (IA-REQ-015/029).
/// </summary>
public sealed record InvitationSummaryView(
    Guid InvitationId,
    string NormalizedEmail,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Guid> RoleIds);

public enum MembershipWriteStatus
{
    Applied,
    NotFound,

    /// <summary>Refused for what the membership is: revoked and asked to reactivate, or the owner's own.</summary>
    Invalid,
    VersionConflict,

    /// <summary>Already in the state the caller asked for, and nothing was written.</summary>
    AlreadyInState
}

public sealed record MembershipWriteResult(MembershipWriteStatus Status, MemberView? Member);

/// <summary>
/// The bounded set of membership reads and writes, behind a port for the same reason roles are: assignment and
/// transfer persistence is not a row operation, and `IApplicationDbContext` deliberately exposes no role or
/// membership-role `DbSet`.
/// <para>
/// It decides nothing. The grant ceiling on a member's roles, the effective-administrator floor and the rule that
/// an owner's membership cannot be suspended or revoked before transfer are business rules and stay in the
/// handlers.
/// </para>
/// </summary>
public interface IMembershipAdministrationStore
{
    /// <summary>One offset page of the tenant's members, in a stable order that ends in the membership's unique identifier.</summary>
    Task<PaginatedList<MemberView>> ListAsync(TenantId tenantId, PaginationQuery pagination, CancellationToken cancellationToken);

    /// <summary>One offset page of the tenant's invitations, in a stable order that ends in the invitation's unique identifier.</summary>
    Task<PaginatedList<InvitationSummaryView>> ListInvitationsAsync(TenantId tenantId, PaginationQuery pagination, CancellationToken cancellationToken);

    Task<MemberView?> FindAsync(TenantId tenantId, Guid membershipId, CancellationToken cancellationToken);

    /// <summary>
    /// Everything the roles named here would confer, so the caller can compare it against its own ceiling. The
    /// whole set is checked on assignment, not just what changed: handing somebody a role is handing them all of
    /// it (IA-REQ-053).
    /// </summary>
    Task<IReadOnlyList<string>?> CodesOfRolesAsync(TenantId tenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>Replaces the member's whole role set and flushes.</summary>
    Task<MembershipWriteResult> ReplaceRolesAsync(TenantId tenantId, Guid membershipId, IReadOnlyList<Guid> roleIds, string version, CancellationToken cancellationToken);

    Task<MembershipWriteResult> SetStatusAsync(TenantId tenantId, Guid membershipId, MembershipStatus target, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the one ownership reference. It flushes with whatever else the caller tracked, which is how the
    /// notice and the audit commit with the transfer or not at all.
    /// </summary>
    Task<MembershipWriteResult> TransferOwnershipAsync(TenantId tenantId, Guid toMembershipId, string version, CancellationToken cancellationToken);

    /// <summary>Which membership owns this organization today, or <see langword="null"/> if none does.</summary>
    Task<Guid?> OwnerAsync(TenantId tenantId, CancellationToken cancellationToken);
}
