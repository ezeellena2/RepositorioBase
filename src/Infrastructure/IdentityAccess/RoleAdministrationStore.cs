using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// How role rows are read and written. It decides nothing: the grant ceiling, the administrator floor and the
/// cancellation of offers naming a widened role are business rules and live in the handlers.
/// </summary>
public sealed class RoleAdministrationStore(ApplicationDbContext context) : IRoleAdministrationStore
{
    /// <summary>Bounds the page whatever the caller asked for, so a route value cannot become a table scan.</summary>
    private const int MaximumLimit = 100;

    public async Task<RolePage> ListAsync(TenantId tenantId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit <= 0 ? MaximumLimit : limit, 1, MaximumLimit);
        var after = Cursor.Decode(cursor);

        // Ordered by the identifier the cursor carries, so a page boundary is stable while roles are being added.
        var query = context.TenantRoles.AsNoTracking().Where(role => role.TenantId == tenantId);
        if (after is { } roleId) query = query.Where(role => role.Id > RoleId.From(roleId));

        var page = await Project(query.OrderBy(role => role.Id)).Take(size + 1).ToListAsync(cancellationToken);
        var items = page.Take(size).ToArray();
        var views = await ViewsAsync(tenantId, items, cancellationToken);
        return new RolePage(views, page.Count > size ? Cursor.Encode(items[^1].RoleId.Value) : null);
    }

    public async Task<RoleView?> FindAsync(TenantId tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        var id = RoleId.From(roleId);
        var role = await Project(context.TenantRoles.AsNoTracking().Where(candidate => candidate.TenantId == tenantId && candidate.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
        return role is null ? null : (await ViewsAsync(tenantId, [role], cancellationToken))[0];
    }

    /// <summary>
    /// Reads the row and its own `xmin` in one query. The token cannot be reached through `Entry` here, because
    /// these reads do not track — and a version read off a detached entity is not the row's version.
    /// </summary>
    private static IQueryable<RoleRow> Project(IQueryable<Role> roles) =>
        roles.Select(role => new RoleRow(
            role.Id,
            role.Name,
            role.IsSystem,
            role.IsRetired,
            EF.Property<uint>(role, "Version")));

    public async Task<IReadOnlyList<string>?> HeldCodesAsync(TenantId tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        var id = RoleId.From(roleId);
        if (!await context.TenantRoles.AnyAsync(role => role.TenantId == tenantId && role.Id == id, cancellationToken)) return null;

        return await context.RolePermissions.AsNoTracking()
            .Where(permission => permission.TenantId == tenantId && permission.RoleId == id)
            .Select(permission => permission.PermissionCode)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// What this actor could put into a role: the codes it effectively holds here, kept to what the catalogue
    /// allows an `Organization`. The intersection is what makes "a code nobody holds can never be granted" true
    /// rather than merely intended.
    /// </summary>
    public async Task<IReadOnlyList<string>> GrantableCodesAsync(TenantId tenantId, Guid actorId, CancellationToken cancellationToken)
    {
        var organizationCodes = Permissions.Catalog
            .Where(definition => definition.AllowedTenantTypes.Contains(TenantType.Organization))
            .Select(definition => definition.Code)
            .ToArray();

        return await (
            from membership in context.TenantMemberships.AsNoTracking()
            join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            join membershipRole in context.MembershipRoles.AsNoTracking()
                on new { membership.TenantId, MembershipId = membership.Id } equals new { membershipRole.TenantId, membershipRole.MembershipId }
            join role in context.TenantRoles.AsNoTracking()
                on new { membershipRole.TenantId, membershipRole.RoleId } equals new { role.TenantId, RoleId = role.Id }
            join rolePermission in context.RolePermissions.AsNoTracking()
                on new { membershipRole.TenantId, membershipRole.RoleId } equals new { rolePermission.TenantId, rolePermission.RoleId }
            where membership.TenantId == tenantId
                && membership.IdentityId == actorId
                && membership.Status == MembershipStatus.Active
                && tenant.Status == TenantStatus.Active
                && !role.IsRetired
                && organizationCodes.Contains(rolePermission.PermissionCode)
            select rolePermission.PermissionCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Distinct identities holding both halves of administration. Counted over the same projection the evaluator
    /// decides with, so the floor cannot report an administrator the evaluator would refuse.
    /// </summary>
    public async Task<int> CountAdministratorsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var held = await (
            from membership in context.TenantMemberships.AsNoTracking()
            join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            join membershipRole in context.MembershipRoles.AsNoTracking()
                on new { membership.TenantId, MembershipId = membership.Id } equals new { membershipRole.TenantId, membershipRole.MembershipId }
            join role in context.TenantRoles.AsNoTracking()
                on new { membershipRole.TenantId, membershipRole.RoleId } equals new { role.TenantId, RoleId = role.Id }
            join rolePermission in context.RolePermissions.AsNoTracking()
                on new { membershipRole.TenantId, membershipRole.RoleId } equals new { rolePermission.TenantId, rolePermission.RoleId }
            where membership.TenantId == tenantId
                && membership.Status == MembershipStatus.Active
                && tenant.Status == TenantStatus.Active
                && !role.IsRetired
                && (rolePermission.PermissionCode == Permissions.RolesManage || rolePermission.PermissionCode == Permissions.MembersManage)
            select new { membership.IdentityId, rolePermission.PermissionCode })
            .Distinct()
            .ToListAsync(cancellationToken);

        return held.GroupBy(entry => entry.IdentityId)
            .Count(group => group.Any(entry => entry.PermissionCode == Permissions.RolesManage)
                         && group.Any(entry => entry.PermissionCode == Permissions.MembersManage));
    }

    public async Task<RoleWriteResult> CreateAsync(TenantId tenantId, RoleEdit edit, CancellationToken cancellationToken)
    {
        var tenant = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        if (tenant is null) return new RoleWriteResult(RoleWriteStatus.Invalid, null);

        Role role;
        try { role = Role.Create(tenant, edit.Name); }
        catch (ArgumentException) { return new RoleWriteResult(RoleWriteStatus.Invalid, null); }

        // The unique index on (TenantId, NormalizedName) is the real arbiter; this read is what lets two
        // administrators racing one name meet the contract's conflict instead of the database's exception.
        if (await context.TenantRoles.AnyAsync(candidate => candidate.TenantId == tenantId && candidate.NormalizedName == role.NormalizedName, cancellationToken))
            return new RoleWriteResult(RoleWriteStatus.VersionConflict, null);

        context.TenantRoles.Add(role);
        if (!await GrantAsync(tenant, role, edit.Codes, cancellationToken)) return new RoleWriteResult(RoleWriteStatus.Invalid, null);

        await context.SaveChangesAsync(cancellationToken);
        return new RoleWriteResult(RoleWriteStatus.Applied, await FindAsync(tenantId, role.Id.Value, cancellationToken));
    }

    public async Task<RoleWriteResult> UpdateAsync(TenantId tenantId, Guid roleId, RoleEdit edit, string version, CancellationToken cancellationToken)
    {
        var (tenant, role, status) = await LoadForWriteAsync(tenantId, roleId, version, cancellationToken);
        if (role is null || tenant is null) return new RoleWriteResult(status, null);
        if (role.IsRetired) return new RoleWriteResult(RoleWriteStatus.Invalid, null);

        try { role.Rename(tenant, edit.Name); }
        catch (ArgumentException) { return new RoleWriteResult(RoleWriteStatus.Invalid, null); }
        catch (InvalidOperationException) { return new RoleWriteResult(RoleWriteStatus.Invalid, null); }

        if (await context.TenantRoles.AnyAsync(candidate => candidate.TenantId == tenantId && candidate.Id != role.Id && candidate.NormalizedName == role.NormalizedName, cancellationToken))
            return new RoleWriteResult(RoleWriteStatus.VersionConflict, null);

        if (!await ReplaceAsync(tenant, role, edit.Codes, cancellationToken)) return new RoleWriteResult(RoleWriteStatus.Invalid, null);

        // The version this contract exposes is the role row's own `xmin`, and PostgreSQL only advances it when the
        // row itself is rewritten. An edit that changes only what the role confers writes nothing but
        // `RolePermissions`, so without this the row keeps the very version two administrators are both editing
        // from and the second silently overwrites the first. Rewriting the row here does two things at once: the
        // version moves, and the token `LoadForWriteAsync` compared travels in this UPDATE's WHERE clause — so a
        // racer that commits in between is refused by the database rather than by a comparison (IA-REQ-035).
        // `Name` and not `IsRetired`: the audit interceptor reads a modified `IsRetired` as a retirement.
        context.Entry(role).Property(candidate => candidate.Name).IsModified = true;

        await context.SaveChangesAsync(cancellationToken);
        return new RoleWriteResult(RoleWriteStatus.Applied, await FindAsync(tenantId, roleId, cancellationToken));
    }

    public async Task<RoleWriteResult> RetireAsync(TenantId tenantId, Guid roleId, string version, CancellationToken cancellationToken)
    {
        var (tenant, role, status) = await LoadForWriteAsync(tenantId, roleId, version, cancellationToken);
        if (role is null || tenant is null) return new RoleWriteResult(status, null);
        if (role.IsRetired) return new RoleWriteResult(RoleWriteStatus.AlreadyInState, null);

        try { role.Retire(tenant); }
        catch (InvalidOperationException) { return new RoleWriteResult(RoleWriteStatus.Invalid, null); }

        await context.SaveChangesAsync(cancellationToken);
        return new RoleWriteResult(RoleWriteStatus.Applied, await FindAsync(tenantId, roleId, cancellationToken));
    }

    /// <summary>
    /// The role, tracked, with the caller's echoed version already compared against the row's own token. A system
    /// role is refused here because it is the tenant's scaffolding, whatever the caller sent.
    /// </summary>
    private async Task<(Tenant? Tenant, Role? Role, RoleWriteStatus Status)> LoadForWriteAsync(
        TenantId tenantId, Guid roleId, string version, CancellationToken cancellationToken)
    {
        var role = await context.TenantRoles.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == RoleId.From(roleId), cancellationToken);
        if (role is null) return (null, null, RoleWriteStatus.NotFound);
        if (role.IsSystem) return (null, null, RoleWriteStatus.Invalid);
        if (!string.Equals(VersionOf(role), version, StringComparison.Ordinal)) return (null, null, RoleWriteStatus.VersionConflict);

        var tenant = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        return tenant is null ? (null, null, RoleWriteStatus.Invalid) : (tenant, role, RoleWriteStatus.Applied);
    }

    private async Task<bool> GrantAsync(Tenant tenant, Role role, IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        if (codes.Count == 0) return true;

        var permissions = await context.Permissions.Where(permission => codes.Contains(permission.Code)).ToListAsync(cancellationToken);
        if (permissions.Count != codes.Count) return false;

        foreach (var permission in permissions)
        {
            // Refuses a code the catalogue does not allow this tenant type, which is the other half of the ceiling.
            try { context.RolePermissions.Add(RolePermission.Create(tenant, role, permission)); }
            catch (InvalidOperationException) { return false; }
        }

        return true;
    }

    /// <summary>
    /// A whole-set replacement, never a per-permission merge: two administrators editing one role from different
    /// starting views would otherwise each apply half of what they meant, and neither would notice.
    /// </summary>
    private async Task<bool> ReplaceAsync(Tenant tenant, Role role, IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        var existing = await context.RolePermissions
            .Where(permission => permission.TenantId == tenant.Id && permission.RoleId == role.Id)
            .ToListAsync(cancellationToken);

        foreach (var permission in existing.Where(permission => !codes.Contains(permission.PermissionCode, StringComparer.Ordinal)))
        {
            permission.Revoke(tenant);
            context.RolePermissions.Remove(permission);
        }

        var kept = existing.Select(permission => permission.PermissionCode).ToHashSet(StringComparer.Ordinal);
        return await GrantAsync(tenant, role, codes.Where(code => !kept.Contains(code)).ToArray(), cancellationToken);
    }

    private async Task<IReadOnlyList<RoleView>> ViewsAsync(TenantId tenantId, IReadOnlyList<RoleRow> roles, CancellationToken cancellationToken)
    {
        if (roles.Count == 0) return [];

        var ids = roles.Select(role => role.RoleId).ToArray();
        var permissions = (await context.RolePermissions.AsNoTracking()
                .Where(permission => permission.TenantId == tenantId && ids.Contains(permission.RoleId))
                .Select(permission => new { permission.RoleId, permission.PermissionCode })
                .ToListAsync(cancellationToken))
            .ToLookup(permission => permission.RoleId, permission => permission.PermissionCode);

        return roles.Select(role => new RoleView(
            role.RoleId.Value,
            role.Name,
            role.IsSystem,
            role.IsRetired,
            permissions[role.RoleId].Order(StringComparer.Ordinal).ToArray(),
            Render(role.Version))).ToArray();
    }

    /// <summary>The row's own `xmin`, rendered as the opaque string the contract echoes.</summary>
    private static string Render(uint version) => version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private string VersionOf(Role role) =>
        Render(context.Entry(role).Property<uint>("Version").CurrentValue);

    private sealed record RoleRow(RoleId RoleId, string Name, bool IsSystem, bool IsRetired, uint Version);

    /// <summary>An opaque continuation. It carries a role identifier and nothing a caller could act on.</summary>
    private static class Cursor
    {
        internal static string Encode(Guid roleId) =>
            Convert.ToBase64String(roleId.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
}
