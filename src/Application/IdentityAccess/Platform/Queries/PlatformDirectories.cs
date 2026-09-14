using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Queries;

/// <summary>
/// An Organization as Platform may see it (IA-REQ-044): identity, lifecycle and concurrency, and nothing about
/// the business inside it. There is deliberately no CUIT, no legal name and no profile payload.
/// </summary>
public sealed record PlatformOrganizationProjection(
    Guid TenantId,
    string Slug,
    string Type,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SuspensionReason,
    DateTimeOffset? SuspendedAtUtc,
    long AuthorizationVersion);

/// <summary>
/// An identity as Platform may see it. The normalized email is present because the directory permission is what
/// gates reaching this projection at all; nothing else about the person is.
/// <para>
/// The account status is here because suspending and reactivating take it as a precondition (IA-REQ-054): an
/// operator sends back the state they read, and the write only lands if the account is still in it. Where the
/// account was before an operator stopped it is deliberately absent — that is what its owner chose about their own
/// account, and it is not part of what an operator needs in order to act.
/// </para>
/// </summary>
public sealed record PlatformIdentityProjection(
    Guid IdentityId,
    string NormalizedEmail,
    string AccountStatus,
    bool EmailConfirmed,
    bool IsLockedOut,
    int MembershipCount,
    string MfaStatus,
    DateTimeOffset? LastSeenUtc);

/// <summary>A Platform administrator as the panel may list one, before inviting or revoking (IA-REQ-042/044).</summary>
public sealed record PlatformAdministratorProjection(
    Guid MembershipId,
    Guid IdentityId,
    string NormalizedEmail,
    bool EmailConfirmed,
    string MembershipStatus,
    string MfaStatus,
    bool IsOwner,
    DateTimeOffset? SinceUtc);

/// <summary>
/// An audit event as Platform may see it. The metadata is the allowlisted reason and outcome only — the stored
/// payload is never handed over, because an audit payload is arbitrary content and this is a read-only projection.
/// </summary>
public sealed record PlatformAuditEventProjection(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    Guid? ActorIdentityId,
    Guid? TenantId,
    string? Outcome,
    string? ReasonCode);

// The four directory reads (IA-REQ-045) each take one offset page request. `PaginationQuery` clamps the page number
// and the page size, so a caller can walk a directory page by page and can never ask for all of it at once; each
// answers one `PaginatedList` page with its totals.

[Authorize(Permissions.PlatformOrganizationsRead, true)]
public sealed record ListPlatformOrganizationsQuery(PaginationQuery Query)
    : IRequest<Result<PaginatedList<PlatformOrganizationProjection>>>;

[Authorize(Permissions.PlatformIdentitiesRead, true)]
public sealed record ListPlatformIdentitiesQuery(PaginationQuery Query)
    : IRequest<Result<PaginatedList<PlatformIdentityProjection>>>;

[Authorize(Permissions.PlatformAdminsRead, true)]
public sealed record ListPlatformAdministratorsQuery(PaginationQuery Query)
    : IRequest<Result<PaginatedList<PlatformAdministratorProjection>>>;

[Authorize(Permissions.PlatformAuditRead, true)]
public sealed record ListPlatformAuditQuery(PaginationQuery Query)
    : IRequest<Result<PaginatedList<PlatformAuditEventProjection>>>;
