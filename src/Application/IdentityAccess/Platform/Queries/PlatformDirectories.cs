using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Queries;

/// <summary>
/// One page request, shared by every Platform directory (IA-REQ-045). The limit is bounded and the cursor is
/// opaque, so a caller can walk a directory and can never ask for all of it, nor for a page keyed on anything it
/// invented.
/// </summary>
public sealed record PlatformDirectoryQuery(int Limit, string? Cursor)
{
    internal const int MinimumLimit = 1;
    internal const int MaximumLimit = 100;

    /// <summary>Clamped rather than refused: a limit outside the range is a client bug, not a security event.</summary>
    internal int BoundedLimit => Math.Clamp(Limit, MinimumLimit, MaximumLimit);
}

/// <summary>One page of a directory. The cursor is null when there is nothing after this page.</summary>
public sealed record PlatformDirectoryPage<T>(IReadOnlyList<T> Items, string? NextCursor);

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
/// </summary>
public sealed record PlatformIdentityProjection(
    Guid IdentityId,
    string NormalizedEmail,
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

[Authorize(Permissions.PlatformOrganizationsRead, true)]
public sealed record ListPlatformOrganizationsQuery(PlatformDirectoryQuery Query)
    : IRequest<Result<PlatformDirectoryPage<PlatformOrganizationProjection>>>;

[Authorize(Permissions.PlatformIdentitiesRead, true)]
public sealed record ListPlatformIdentitiesQuery(PlatformDirectoryQuery Query)
    : IRequest<Result<PlatformDirectoryPage<PlatformIdentityProjection>>>;

[Authorize(Permissions.PlatformAdminsRead, true)]
public sealed record ListPlatformAdministratorsQuery(PlatformDirectoryQuery Query)
    : IRequest<Result<PlatformDirectoryPage<PlatformAdministratorProjection>>>;

[Authorize(Permissions.PlatformAuditRead, true)]
public sealed record ListPlatformAuditQuery(PlatformDirectoryQuery Query)
    : IRequest<Result<PlatformDirectoryPage<PlatformAuditEventProjection>>>;
