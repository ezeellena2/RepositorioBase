using CleanArchitecture.Application.IdentityAccess.Members;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// How membership rows are read and written. It decides nothing: the grant ceiling on assignment, the
/// administrator floor and the owner rule are business rules and live in the handlers.
/// </summary>
public sealed class MembershipAdministrationStore(ApplicationDbContext context) : IMembershipAdministrationStore
{
    private const int MaximumLimit = 100;

    public async Task<MemberPage> ListAsync(TenantId tenantId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit <= 0 ? MaximumLimit : limit, 1, MaximumLimit);
        var after = OpaqueCursor.Decode(cursor);
        var owner = await OwnerAsync(tenantId, cancellationToken);

        var query = context.TenantMemberships.AsNoTracking().Where(membership => membership.TenantId == tenantId);
        if (after is { } membershipId) query = query.Where(membership => membership.Id > MembershipId.From(membershipId));

        var page = await Project(query.OrderBy(membership => membership.Id), tenantId).Take(size + 1).ToListAsync(cancellationToken);
        var items = page.Take(size).ToArray();
        return new MemberPage(
            await ViewsAsync(tenantId, items, owner, cancellationToken),
            page.Count > size ? OpaqueCursor.Encode(items[^1].MembershipId) : null);
    }

    public async Task<InvitationSummaryPage> ListInvitationsAsync(TenantId tenantId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit <= 0 ? MaximumLimit : limit, 1, MaximumLimit);
        var after = OpaqueCursor.Decode(cursor);

        var query = context.Invitations.AsNoTracking().Where(invitation => invitation.TenantId == tenantId);
        if (after is { } invitationId) query = query.Where(invitation => invitation.Id > InvitationId.From(invitationId));

        var page = await query.OrderBy(invitation => invitation.Id).Take(size + 1)
            .Select(invitation => new
            {
                InvitationId = invitation.Id.Value,
                invitation.NormalizedEmail,
                Status = invitation.Status.ToString(),
                invitation.CreatedAt,
                invitation.ExpiresAt
            })
            .ToListAsync(cancellationToken);

        var items = page.Take(size).ToArray();
        var ids = items.Select(item => InvitationId.From(item.InvitationId)).ToArray();
        var offered = (await context.InvitationRoles.AsNoTracking()
                .Where(link => link.TenantId == tenantId && ids.Contains(link.InvitationId))
                .Select(link => new { link.InvitationId, link.RoleId })
                .ToListAsync(cancellationToken))
            .ToLookup(link => link.InvitationId.Value, link => link.RoleId.Value);

        // No token, no envelope, nothing usable to accept with — only what the offer is and who it is for.
        var views = items.Select(item => new InvitationSummaryView(
            item.InvitationId,
            item.NormalizedEmail,
            item.Status,
            item.CreatedAt,
            item.ExpiresAt,
            offered[item.InvitationId].Order().ToArray())).ToArray();

        return new InvitationSummaryPage(views, page.Count > size ? OpaqueCursor.Encode(items[^1].InvitationId) : null);
    }

    public async Task<MemberView?> FindAsync(TenantId tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        var id = MembershipId.From(membershipId);
        var row = await Project(context.TenantMemberships.AsNoTracking().Where(membership => membership.TenantId == tenantId && membership.Id == id), tenantId)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var views = await ViewsAsync(tenantId, [row], await OwnerAsync(tenantId, cancellationToken), cancellationToken);
        return views[0];
    }

    public async Task<IReadOnlyList<string>?> CodesOfRolesAsync(TenantId tenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return [];

        var ids = roleIds.Select(RoleId.From).ToArray();

        // Every named role must exist here, be this tenant's and not be retired. A missing one is answered as
        // absent rather than skipped, because silently assigning a subset is not what the caller asked for.
        var found = await context.TenantRoles.AsNoTracking()
            .Where(role => role.TenantId == tenantId && ids.Contains(role.Id) && !role.IsRetired)
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);
        if (found.Count != roleIds.Count) return null;

        return await context.RolePermissions.AsNoTracking()
            .Where(permission => permission.TenantId == tenantId && ids.Contains(permission.RoleId))
            .Select(permission => permission.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<MembershipWriteResult> ReplaceRolesAsync(TenantId tenantId, Guid membershipId, IReadOnlyList<Guid> roleIds, string version, CancellationToken cancellationToken)
    {
        var (tenant, membership, status) = await LoadForWriteAsync(tenantId, membershipId, version, cancellationToken);
        if (tenant is null || membership is null) return new MembershipWriteResult(status, null);

        var wanted = roleIds.Select(RoleId.From).ToHashSet();
        var existing = await context.MembershipRoles
            .Where(link => link.TenantId == tenantId && link.MembershipId == membership.Id)
            .ToListAsync(cancellationToken);

        context.MembershipRoles.RemoveRange(existing.Where(link => !wanted.Contains(link.RoleId)));
        var kept = existing.Select(link => link.RoleId).ToHashSet();
        foreach (var roleId in wanted.Where(id => !kept.Contains(id)))
        {
            var role = await context.TenantRoles.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == roleId, cancellationToken);
            if (role is null || role.IsRetired) return new MembershipWriteResult(MembershipWriteStatus.Invalid, null);
            context.MembershipRoles.Add(MembershipRole.Create(tenant, membership, role));
        }

        // Every association change has to move the tenant's version, or the evaluator would keep answering from
        // the authority the member held a moment ago.
        tenant.IncrementAuthorizationVersion();

        // …and the membership's own version, which is this row's `xmin`. Changing only which roles somebody holds
        // writes nothing but `MembershipRoles`, so without rewriting the row the version this contract hands out
        // is still the one both administrators read. The rewrite carries that same token in its WHERE clause, so
        // a racer that commits between the version check and here loses to the database (IA-REQ-035).
        MoveVersionOf(membership);

        await context.SaveChangesAsync(cancellationToken);
        return new MembershipWriteResult(MembershipWriteStatus.Applied, await FindAsync(tenantId, membershipId, cancellationToken));
    }

    public async Task<MembershipWriteResult> SetStatusAsync(TenantId tenantId, Guid membershipId, MembershipStatus target, string version, CancellationToken cancellationToken)
    {
        var (tenant, membership, status) = await LoadForWriteAsync(tenantId, membershipId, version, cancellationToken);
        if (tenant is null || membership is null) return new MembershipWriteResult(status, null);
        if (membership.Status == target) return new MembershipWriteResult(MembershipWriteStatus.AlreadyInState, null);

        try
        {
            switch (target)
            {
                case MembershipStatus.Suspended: membership.Suspend(tenant); break;
                case MembershipStatus.Active: membership.Reactivate(tenant); break;
                case MembershipStatus.Revoked: membership.Revoke(tenant); break;
                default: return new MembershipWriteResult(MembershipWriteStatus.Invalid, null);
            }
        }
        catch (InvalidOperationException)
        {
            // A transition the aggregate refuses — reactivating something revoked, most of all. It is a state a
            // caller can reach, so it is answered rather than thrown.
            return new MembershipWriteResult(MembershipWriteStatus.Invalid, null);
        }

        // Revoking ends the membership, so the authority it carried goes with it. The rows are removed rather
        // than left dormant: a later reinstatement comes through a fresh invitation, which offers roles again.
        if (target == MembershipStatus.Revoked)
        {
            context.MembershipRoles.RemoveRange(
                await context.MembershipRoles.Where(link => link.TenantId == tenantId && link.MembershipId == membership.Id).ToListAsync(cancellationToken));
        }

        await context.SaveChangesAsync(cancellationToken);
        return new MembershipWriteResult(MembershipWriteStatus.Applied, null);
    }

    public async Task<MembershipWriteResult> TransferOwnershipAsync(TenantId tenantId, Guid toMembershipId, string version, CancellationToken cancellationToken)
    {
        var (tenant, membership, status) = await LoadForWriteAsync(tenantId, toMembershipId, version, cancellationToken);
        if (tenant is null || membership is null) return new MembershipWriteResult(status, null);
        if (tenant.OwnerMembershipId == membership.Id) return new MembershipWriteResult(MembershipWriteStatus.AlreadyInState, null);

        var formerOwnerId = tenant.OwnerMembershipId;
        try { tenant.TransferOwnershipTo(membership); }
        catch (InvalidOperationException) { return new MembershipWriteResult(MembershipWriteStatus.Invalid, null); }

        // `isOwner` is not a column on either membership — it is what the tenant's single reference says about
        // them — so this write would otherwise touch neither row, and both would keep handing out a version that
        // describes a member view which has stopped being true. Whoever received the organization and whoever
        // gave it up are each rewritten, so each version moves with what it describes (IA-REQ-035).
        MoveVersionOf(membership);
        if (formerOwnerId is { } former)
        {
            var previous = await context.TenantMemberships
                .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == former, cancellationToken);
            if (previous is not null) MoveVersionOf(previous);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new MembershipWriteResult(MembershipWriteStatus.Applied, null);
    }

    /// <summary>
    /// Rewrites a membership row so PostgreSQL advances the `xmin` this contract hands out as its version. Some
    /// changes to what a member view says are not changes to any column on that row — which roles they hold, and
    /// whether they own the organization — and a version that did not move for them is a version that lies.
    /// `Status` and not another column because the audit interceptor reads a modified `Status` as no particular
    /// transition, while it reads a modified `IsRetired` on a role as a retirement.
    /// </summary>
    private void MoveVersionOf(TenantMembership membership) =>
        context.Entry(membership).Property(candidate => candidate.Status).IsModified = true;

    public async Task<Guid?> OwnerAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await context.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.OwnerMembershipId.HasValue ? tenant.OwnerMembershipId.Value.Value : (Guid?)null)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The membership, tracked, with the caller's echoed version already compared against its own token.</summary>
    private async Task<(Tenant? Tenant, TenantMembership? Membership, MembershipWriteStatus Status)> LoadForWriteAsync(
        TenantId tenantId, Guid membershipId, string version, CancellationToken cancellationToken)
    {
        var id = MembershipId.From(membershipId);
        var membership = await context.TenantMemberships.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == id, cancellationToken);
        if (membership is null) return (null, null, MembershipWriteStatus.NotFound);
        if (!string.Equals(Render(context.Entry(membership).Property<uint>("Version").CurrentValue), version, StringComparison.Ordinal))
            return (null, null, MembershipWriteStatus.VersionConflict);

        var tenant = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        return tenant is null ? (null, null, MembershipWriteStatus.Invalid) : (tenant, membership, MembershipWriteStatus.Applied);
    }

    /// <summary>
    /// Reads the row, its own `xmin` and the identity behind it in one query, because these reads do not track
    /// and a version read off a detached entity is not the row's version.
    /// </summary>
    private IQueryable<MemberRow> Project(IQueryable<TenantMembership> memberships, TenantId tenantId) =>
        from membership in memberships
        join user in context.Users.AsNoTracking() on membership.IdentityId equals user.Id
        join profile in context.PersonProfiles.AsNoTracking() on membership.IdentityId equals profile.IdentityId into profiles
        from profile in profiles.DefaultIfEmpty()
        select new MemberRow(
            membership.Id.Value,
            membership.IdentityId,
            profile == null ? null : profile.DisplayName,
            user.Email,
            membership.Status.ToString(),
            EF.Property<uint>(membership, "Version"));

    private async Task<IReadOnlyList<MemberView>> ViewsAsync(TenantId tenantId, IReadOnlyList<MemberRow> rows, Guid? owner, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(row => MembershipId.From(row.MembershipId)).ToArray();
        var assigned = (await context.MembershipRoles.AsNoTracking()
                .Where(link => link.TenantId == tenantId && ids.Contains(link.MembershipId))
                .Select(link => new { link.MembershipId, link.RoleId })
                .ToListAsync(cancellationToken))
            .ToLookup(link => link.MembershipId.Value, link => link.RoleId.Value);

        return rows.Select(row => new MemberView(
            row.MembershipId,
            row.IdentityId,
            // The email stands in only for an identity that has told us no name, exactly as the context does.
            string.IsNullOrWhiteSpace(row.DisplayName) ? row.Email ?? string.Empty : row.DisplayName,
            row.Email ?? string.Empty,
            row.Status,
            assigned[row.MembershipId].Order().ToArray(),
            owner == row.MembershipId,
            Render(row.Version))).ToArray();
    }

    private static string Render(uint version) => version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed record MemberRow(Guid MembershipId, Guid IdentityId, string? DisplayName, string? Email, string Status, uint Version);
}

/// <summary>An opaque continuation carrying a row identifier and nothing a caller could act on.</summary>
internal static class OpaqueCursor
{
    internal static string Encode(Guid id) =>
        Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static Guid? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            var bytes = Convert.FromBase64String(padded);
            // An all-zero identifier is not a position either: the strongly-typed identifiers refuse it by
            // throwing, and a probe with sixteen zero bytes is well-formed base64url, so decoding it as a
            // value would turn a harmless guess into a sanitized 500.
            var decoded = bytes.Length == 16 ? new Guid(bytes) : (Guid?)null;
            return decoded == Guid.Empty ? null : decoded;
        }
        catch (FormatException)
        {
            // An unreadable cursor is not an error to explain; it is simply not a position in this list.
            return null;
        }
    }
}
