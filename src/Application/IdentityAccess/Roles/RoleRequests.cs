using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Roles;

/// <summary>
/// Custom roles inside one `Organization` (IA-REQ-053).
/// <para>
/// Every request names the tenant it is for and every handler compares that name against the session's active
/// tenant rather than trusting it: the pipeline authorizes against the session, so honouring a different tenant
/// would be authorizing against one and acting on another.
/// </para>
/// </summary>
[Authorize(Permissions.RolesRead, true)]
public sealed record GetPermissionCatalogQuery(TenantId TenantId) : IRequest<Result<IReadOnlyList<PermissionCatalogEntry>>>;

[Authorize(Permissions.RolesRead, true)]
public sealed record ListRolesQuery(TenantId TenantId, int Limit, string? Cursor) : IRequest<Result<RolePage>>;

[Authorize(Permissions.RolesRead, true)]
public sealed record GetRoleQuery(TenantId TenantId, Guid RoleId) : IRequest<Result<RoleView>>;

/// <summary>
/// Creating a role is granting authority, so it is sensitive and needs a live proof (amendment D2): otherwise
/// `roles.manage` is a super-permission whose holder can package everything they hold into a role and assign it,
/// and one stolen session is the whole tenant.
/// </summary>
[Authorize(Permissions.RolesManage, true)]
public sealed record CreateRoleCommand(TenantId TenantId, string? Name, IReadOnlyList<string>? PermissionCodes)
    : IRequest<Result<RoleView>>, ISensitiveRequest;

[Authorize(Permissions.RolesManage, true)]
public sealed record UpdateRoleCommand(TenantId TenantId, Guid RoleId, string? Name, IReadOnlyList<string>? PermissionCodes, string? Version)
    : IRequest<Result<RoleView>>, ISensitiveRequest;

[Authorize(Permissions.RolesManage, true)]
public sealed record RetireRoleCommand(TenantId TenantId, Guid RoleId, string? Version)
    : IRequest<Result>, ISensitiveRequest;
