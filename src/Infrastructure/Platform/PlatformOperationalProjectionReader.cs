using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Interceptors;
using CleanArchitecture.Infrastructure.Data.Pagination;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Platform;

/// <summary>
/// The four Platform directories, read straight out of PostgreSQL into their projections (IA-REQ-044/045).
/// <para>
/// Every query projects into the record before leaving the database, so the columns the allowlist forbids are
/// never even loaded. That is stronger than mapping afterwards: there is no in-memory object holding a CUIT or a
/// password hash for a later change to accidentally serialize.
/// </para>
/// <para>
/// Paging is by offset over an order that ends in each row's unique key: the row identifier, or for the audit log
/// the moment it happened and then the identifier. Each read counts the directory and reads one bounded page, so a
/// caller knows the totals. A page that shifts because rows changed between two reads is corrected by the caller
/// and settled by the next read.
/// </para>
/// </summary>
public sealed class PlatformOperationalProjectionReader(ApplicationDbContext context) : IPlatformOperationalProjectionReader
{
    public async Task<PaginatedList<PlatformOrganizationProjection>> ReadOrganizationsAsync(PaginationQuery query, CancellationToken cancellationToken) =>
        await context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Type == TenantType.Organization)
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => new PlatformOrganizationProjection(
                tenant.Id.Value,
                tenant.Slug.Value,
                tenant.Type.ToString(),
                tenant.Status.ToString(),
                EF.Property<DateTimeOffset>(tenant, OperationalTimestampInterceptor.CreatedAt),
                EF.Property<DateTimeOffset>(tenant, OperationalTimestampInterceptor.UpdatedAt),
                tenant.SuspensionReason == null ? null : tenant.SuspensionReason.ToString(),
                tenant.SuspendedAt,
                tenant.AuthorizationVersion))
            .ToPaginatedListAsync(query, cancellationToken);

    public async Task<PaginatedList<PlatformIdentityProjection>> ReadIdentitiesAsync(PaginationQuery query, CancellationToken cancellationToken) =>
        await context.Set<ApplicationUser>()
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => new PlatformIdentityProjection(
                user.Id,
                user.NormalizedEmail!,
                user.Status.ToString(),
                user.EmailConfirmed,
                user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow,
                context.TenantMemberships.Count(membership => membership.IdentityId == user.Id),
                context.PlatformMfaEnrollments
                    .Where(enrollment => enrollment.IdentityId == user.Id)
                    .Select(enrollment => enrollment.Status.ToString())
                    .FirstOrDefault() ?? "None",
                context.UserSessions
                    .Where(session => session.IdentityId == user.Id)
                    .Max(session => (DateTimeOffset?)session.LastSeenAt)))
            .ToPaginatedListAsync(query, cancellationToken);

    public async Task<PaginatedList<PlatformAdministratorProjection>> ReadAdministratorsAsync(PaginationQuery query, CancellationToken cancellationToken)
    {
        var ownerRole = PlatformRoles.Owner.ToUpperInvariant();

        return await context.TenantMemberships
            .AsNoTracking()
            .Where(membership => context.Tenants.Any(tenant => tenant.Id == membership.TenantId && tenant.Type == TenantType.Platform))
            .OrderBy(membership => membership.Id)
            .Select(row => new PlatformAdministratorProjection(
                row.Id.Value,
                row.IdentityId,
                context.Set<ApplicationUser>().Where(user => user.Id == row.IdentityId).Select(user => user.NormalizedEmail!).FirstOrDefault()!,
                context.Set<ApplicationUser>().Where(user => user.Id == row.IdentityId).Select(user => user.EmailConfirmed).FirstOrDefault(),
                row.Status.ToString(),
                context.PlatformMfaEnrollments
                    .Where(enrollment => enrollment.IdentityId == row.IdentityId)
                    .Select(enrollment => enrollment.Status.ToString())
                    .FirstOrDefault() ?? "None",
                context.MembershipRoles.Any(assignment =>
                    assignment.MembershipId == row.Id &&
                    context.TenantRoles.Any(role => role.Id == assignment.RoleId && role.NormalizedName == ownerRole)),
                // When they became one: the moment the invitation that granted this membership was consumed.
                context.PlatformAdminInvitations
                    .Where(invitation => invitation.BoundIdentityId == row.IdentityId && invitation.AcceptedAt != null)
                    .Select(invitation => invitation.AcceptedAt)
                    .FirstOrDefault()))
            .ToPaginatedListAsync(query, cancellationToken);
    }

    public async Task<PaginatedList<PlatformAuditEventProjection>> ReadAuditAsync(PaginationQuery query, CancellationToken cancellationToken)
    {
        // Newest first, and ordered by when it happened rather than by the row identifier. An audit directory
        // ordered by a random UUID is one where a new event lands in an arbitrary position, which makes reading
        // the log impossible and makes a bounded page show an arbitrary slice of history. The identifier is
        // still part of the order, so two events recorded in the same microsecond page stably.
        var page = await context.AuditEvents
            .AsNoTracking()
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Select(auditEvent => new
            {
                auditEvent.Id,
                auditEvent.EventType,
                auditEvent.OccurredAt,
                auditEvent.CorrelationId,
                auditEvent.ActorId,
                auditEvent.TenantId,
                auditEvent.Metadata
            })
            .ToPaginatedListAsync(query, cancellationToken);

        // Only the two allowlisted metadata keys leave this method. The rest of the payload is arbitrary
        // content, and a read-only security projection must not carry it (IA-REQ-044).
        var items = page.Items.Select(row => new PlatformAuditEventProjection(
            row.Id,
            row.EventType,
            row.OccurredAt,
            row.CorrelationId,
            row.ActorId,
            row.TenantId?.Value,
            row.Metadata.TryGetValue("outcome", out var outcome) ? outcome : null,
            row.Metadata.TryGetValue("code", out var code) ? code : null)).ToArray();

        return new PaginatedList<PlatformAuditEventProjection>(items, page.PageNumber, page.PageSize, page.TotalCount);
    }
}
