using System.Text;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Interceptors;
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
/// Paging is keyed on the row's own identifier rather than an offset. An offset would skip or repeat rows as the
/// underlying set changes, and the cursor is opaque so a caller cannot turn it into a filter.
/// </para>
/// </summary>
public sealed class PlatformOperationalProjectionReader(ApplicationDbContext context) : IPlatformOperationalProjectionReader
{
    public async Task<PlatformDirectoryPage<PlatformOrganizationProjection>> ReadOrganizationsAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken)
    {
        var after = DecodeGuid(query.Cursor);
        var limit = Bounded(query);

        var afterId = after is { } value ? TenantId.From(value) : (TenantId?)null;
        var items = await context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Type == TenantType.Organization)
            .Where(tenant => afterId == null || tenant.Id > afterId.Value)
            .OrderBy(tenant => tenant.Id)
            .Take(limit + 1)
            .Select(tenant => new PlatformOrganizationProjection(
                tenant.Id.Value,
                tenant.Slug.Value,
                tenant.Type.ToString(),
                tenant.Status.ToString(),
                EF.Property<DateTimeOffset>(tenant, TenantTimestampInterceptor.CreatedAt),
                EF.Property<DateTimeOffset>(tenant, TenantTimestampInterceptor.UpdatedAt),
                tenant.SuspensionReason == null ? null : tenant.SuspensionReason.ToString(),
                tenant.SuspendedAt,
                tenant.AuthorizationVersion))
            .ToListAsync(cancellationToken);

        return Page(items, limit, item => item.TenantId);
    }

    public async Task<PlatformDirectoryPage<PlatformIdentityProjection>> ReadIdentitiesAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken)
    {
        var after = DecodeGuid(query.Cursor);
        var limit = Bounded(query);

        var items = await context.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(user => after == null || user.Id > after)
            .OrderBy(user => user.Id)
            .Take(limit + 1)
            .Select(user => new PlatformIdentityProjection(
                user.Id,
                user.NormalizedEmail!,
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
            .ToListAsync(cancellationToken);

        return Page(items, limit, item => item.IdentityId);
    }

    public async Task<PlatformDirectoryPage<PlatformAdministratorProjection>> ReadAdministratorsAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken)
    {
        var after = DecodeGuid(query.Cursor);
        var limit = Bounded(query);
        var ownerRole = PlatformRoles.Owner.ToUpperInvariant();
        var afterId = after is { } value ? MembershipId.From(value) : (MembershipId?)null;

        var items = await context.TenantMemberships
            .AsNoTracking()
            .Where(membership => context.Tenants.Any(tenant => tenant.Id == membership.TenantId && tenant.Type == TenantType.Platform))
            .Where(membership => afterId == null || membership.Id > afterId.Value)
            .OrderBy(membership => membership.Id)
            .Take(limit + 1)
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
            .ToListAsync(cancellationToken);

        return Page(items, limit, item => item.MembershipId);
    }

    public async Task<PlatformDirectoryPage<PlatformAuditEventProjection>> ReadAuditAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken)
    {
        var limit = Bounded(query);
        var after = DecodeMoment(query.Cursor);

        // Newest first, and keyed on when it happened rather than on the row identifier. An audit directory
        // ordered by a random UUID is one where a new event lands in an arbitrary position, which makes reading
        // the log impossible and makes a bounded page show an arbitrary slice of history. The identifier is
        // still part of the key, so two events recorded in the same microsecond order stably.
        var rows = await context.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => after == null ||
                                 auditEvent.OccurredAt < after.Value.OccurredAt ||
                                 (auditEvent.OccurredAt == after.Value.OccurredAt && auditEvent.Id < after.Value.Id))
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Take(limit + 1)
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
            .ToListAsync(cancellationToken);

        // Only the two allowlisted metadata keys leave this method. The rest of the payload is arbitrary
        // content, and a read-only security projection must not carry it (IA-REQ-044).
        var items = rows.Select(row => new PlatformAuditEventProjection(
            row.Id,
            row.EventType,
            row.OccurredAt,
            row.CorrelationId,
            row.ActorId,
            row.TenantId?.Value,
            row.Metadata.TryGetValue("outcome", out var outcome) ? outcome : null,
            row.Metadata.TryGetValue("code", out var code) ? code : null)).ToList();

        if (items.Count <= limit) return new PlatformDirectoryPage<PlatformAuditEventProjection>(items, null);
        var page = items.Take(limit).ToArray();
        return new PlatformDirectoryPage<PlatformAuditEventProjection>(
            page,
            EncodeMoment(page[^1].OccurredAtUtc, page[^1].EventId));
    }

    private static int Bounded(PlatformDirectoryQuery query) => Math.Clamp(query.Limit, 1, 100);

    /// <summary>
    /// Takes one row more than the page, so the presence of a next page is known without a second count query, and
    /// hands back a cursor only when there actually is one.
    /// </summary>
    private static PlatformDirectoryPage<T> Page<T>(List<T> items, int limit, Func<T, Guid> key)
    {
        if (items.Count <= limit)
        {
            return new PlatformDirectoryPage<T>(items, null);
        }

        var page = items.Take(limit).ToArray();
        return new PlatformDirectoryPage<T>(page, EncodeGuid(key(page[^1])));
    }

    /// <summary>A cursor for a directory ordered by time, which needs both halves of its key.</summary>
    private static (DateTimeOffset OccurredAt, Guid Id)? DecodeMoment(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2 &&
                   DateTimeOffset.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var occurredAt) &&
                   Guid.TryParse(parts[1], out var id)
                ? (occurredAt, id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string EncodeMoment(DateTimeOffset occurredAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{occurredAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}|{id}"));

    private static Guid? DecodeGuid(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            return Guid.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), out var value) ? value : null;
        }
        catch (FormatException)
        {
            // A cursor the server did not issue simply starts from the beginning. Refusing it would turn the
            // opaque token into something a caller can probe.
            return null;
        }
    }

    private static string EncodeGuid(Guid value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString()));
}
