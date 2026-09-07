using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Roles;

/// <summary>One role as an administrator sees it. `Version` is opaque and is the row's own concurrency token.</summary>
public sealed record RoleView(
    Guid RoleId,
    string Name,
    bool IsSystem,
    bool IsRetired,
    IReadOnlyList<string> Permissions,
    string Version);

/// <summary>A page of roles, bounded by the caller's `limit` and continued by an opaque cursor.</summary>
public sealed record RolePage(IReadOnlyList<RoleView> Items, string? NextCursor);

/// <summary>
/// One entry of the catalogue as this caller sees it: the code, and whether this caller could grant it. The
/// second half is the grant-time ceiling made visible, so a screen can offer what will be accepted instead of
/// letting somebody compose a role the server will refuse (IA-REQ-053).
/// </summary>
public sealed record PermissionCatalogEntry(string Code, bool Grantable);

/// <summary>What a role should become: its name and its whole permission set, never a set of changes to one.</summary>
public sealed record RoleEdit(string Name, IReadOnlyList<string> Codes);

public enum RoleWriteStatus
{
    Applied,

    /// <summary>No such role in this tenant. A role of another tenant reaches the caller this way (IA-REQ-030).</summary>
    NotFound,

    /// <summary>Refused for what the role or the request is: a system role, a retired one, an unusable name.</summary>
    Invalid,

    /// <summary>The echoed version is not the row's, so somebody else changed it since the caller read it.</summary>
    VersionConflict,

    /// <summary>The role already holds the state the caller asked for, and nothing was written.</summary>
    AlreadyInState
}

public sealed record RoleWriteResult(RoleWriteStatus Status, RoleView? Role);

/// <summary>
/// The bounded set of role reads and writes, kept behind a port rather than reached through raw association
/// `DbSet`s — which is also why `IApplicationDbContext` exposes none of them.
/// <para>
/// The division of labour is deliberate. This port knows how role rows are read and written, including the
/// whole-set replacement and the row's own concurrency token. It decides nothing: the grant-time ceiling, the
/// effective-administrator floor and the cancellation of offers naming a widened role are business rules and stay
/// in the handlers, which is why the two counting reads are here as reads rather than as guards.
/// </para>
/// </summary>
public interface IRoleAdministrationStore
{
    Task<RolePage> ListAsync(TenantId tenantId, int limit, string? cursor, CancellationToken cancellationToken);

    Task<RoleView?> FindAsync(TenantId tenantId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>The codes this role currently confers, or <see langword="null"/> when the tenant has no such role.</summary>
    Task<IReadOnlyList<string>?> HeldCodesAsync(TenantId tenantId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// The codes the actor effectively holds in this tenant, intersected with what the catalogue allows the tenant
    /// type. It is the ceiling itself, so it is read inside the mutating transaction and never cached.
    /// </summary>
    Task<IReadOnlyList<string>> GrantableCodesAsync(TenantId tenantId, Guid actorId, CancellationToken cancellationToken);

    /// <summary>
    /// How many distinct identities hold both halves of administration, over the state the transaction has
    /// flushed. Lockout is excluded on purpose: it is a temporary condition, and counting it would let an
    /// administrator make a tenant unadministrable by mistyping a password.
    /// </summary>
    Task<int> CountAdministratorsAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The same count, asked as "how many would be left if this identity could no longer act". Parking an account
    /// leaves its memberships exactly where they were, so the count over the rows cannot notice — which is
    /// precisely why the question has to be asked before the state changes rather than after it (IA-REQ-054).
    /// </summary>
    Task<int> CountAdministratorsExceptAsync(TenantId tenantId, Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the role with exactly these codes and flushes. The caller has already applied the ceiling; what is
    /// refused here is a name the domain will not take and a normalized name this tenant already uses.
    /// </summary>
    Task<RoleWriteResult> CreateAsync(TenantId tenantId, RoleEdit edit, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the role and replaces its whole permission set, then flushes — together with whatever else the
    /// caller has tracked in this transaction, which is how the cancelled offers commit with the widening.
    /// </summary>
    Task<RoleWriteResult> UpdateAsync(TenantId tenantId, Guid roleId, RoleEdit edit, string version, CancellationToken cancellationToken);

    Task<RoleWriteResult> RetireAsync(TenantId tenantId, Guid roleId, string version, CancellationToken cancellationToken);
}
