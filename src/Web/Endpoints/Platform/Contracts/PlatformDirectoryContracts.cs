using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;

namespace CleanArchitecture.Web.PlatformEndpoints.Contracts;

/// <summary>
/// The four Platform projections as they cross HTTP (IA-REQ-044). Each is declared separately rather than shared,
/// so that widening one can never silently widen another, and each carries only what the allowlist permits.
/// </summary>
public sealed record PlatformOrganizationResponse(
    Guid TenantId,
    string Slug,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SuspensionReason,
    DateTimeOffset? SuspendedAt,
    long AuthorizationVersion);

public sealed record PlatformIdentityResponse(
    Guid IdentityId,
    string NormalizedEmail,
    string AccountStatus,
    bool EmailConfirmed,
    bool IsLockedOut,
    int MembershipCount,
    string MfaStatus,
    DateTimeOffset? LastSeen);

public sealed record PlatformAdministratorResponse(
    Guid MembershipId,
    Guid IdentityId,
    string NormalizedEmail,
    bool EmailConfirmed,
    string MembershipStatus,
    string MfaStatus,
    bool IsOwner,
    DateTimeOffset? Since);

public sealed record PlatformAuditEventResponse(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    Guid? ActorIdentityId,
    Guid? TenantId,
    string? Outcome,
    string? ReasonCode);

/// <summary>
/// One offset page of each directory: its items with <c>pageNumber</c>, <c>pageSize</c>, <c>totalCount</c>,
/// <c>totalPages</c>, <c>hasPreviousPage</c> and <c>hasNextPage</c>. They are four types rather than one generic
/// envelope because IA-REQ-045 makes each directory its own typed resource, and because a shared envelope is exactly
/// the pagination wrapper the identity endpoints are required not to have.
/// </summary>
public sealed record PlatformOrganizationDirectoryResponse(
    IReadOnlyList<PlatformOrganizationResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PlatformOrganizationDirectoryResponse From(
        PaginatedList<PlatformOrganizationProjection> page, Func<PlatformOrganizationProjection, PlatformOrganizationResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record PlatformIdentityDirectoryResponse(
    IReadOnlyList<PlatformIdentityResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PlatformIdentityDirectoryResponse From(
        PaginatedList<PlatformIdentityProjection> page, Func<PlatformIdentityProjection, PlatformIdentityResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record PlatformAdministratorDirectoryResponse(
    IReadOnlyList<PlatformAdministratorResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PlatformAdministratorDirectoryResponse From(
        PaginatedList<PlatformAdministratorProjection> page, Func<PlatformAdministratorProjection, PlatformAdministratorResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

public sealed record PlatformAuditDirectoryResponse(
    IReadOnlyList<PlatformAuditEventResponse> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PlatformAuditDirectoryResponse From(
        PaginatedList<PlatformAuditEventProjection> page, Func<PlatformAuditEventProjection, PlatformAuditEventResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}

/// <summary>
/// Suspending needs the reason it is being suspended for; reactivating needs nothing, and deliberately takes the
/// same body so a client has one shape to send (IA-REQ-043).
/// <para>
/// The reason crosses the wire as the name of a closed set rather than as the enum itself. That is the same form
/// the projection answers with, so a client reads and writes one vocabulary; and it keeps the endpoint able to
/// refuse an unknown value as a business refusal instead of a body it could not bind.
/// </para>
/// </summary>
public sealed record PlatformTenantLifecycleRequest(string? Reason);

/// <summary>
/// Stopping one account. `ExpectedStatus` is a precondition, not a hint: it is the state the operator read in the
/// directory, and the write only lands if the account is still in it (IA-REQ-054).
/// </summary>
public sealed record SuspendIdentityRequest(string? Reason, string? ExpectedStatus);

/// <summary>
/// Letting one account go again. `AcknowledgeSelfDeactivation` is the operator saying they know the account will
/// land back in the state its owner chose, rather than in `Active`.
/// </summary>
public sealed record ReactivateIdentityRequest(string? ExpectedStatus, bool AcknowledgeSelfDeactivation);

/// <summary>
/// Resolving one documentary dispute. `outcome` is `corrected` or `rejected`, and `evidenceReference` names a case
/// held outside this system — carrying no free text and nothing about the person (IA-REQ-058).
/// </summary>
public sealed record ResolveDocumentDisputeRequest(string? Outcome, string? EvidenceReference);

/// <summary>Who to invite as a Platform administrator. There is no role field: the role is the system one.</summary>
public sealed record PlatformAdministratorInvitationRequest(string Email);
