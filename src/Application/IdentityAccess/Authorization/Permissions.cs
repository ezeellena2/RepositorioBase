using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

public static class Permissions
{
    public const string MembersRead = "members.read";
    public const string MembersManage = "members.manage";
    public const string MembersInvite = "members.invite";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string TenantRead = "tenant.read";
    public const string TenantManage = "tenant.manage";
    public const string PlatformAdminsRead = "platform.admins.read";
    public const string PlatformAdminsManage = "platform.admins.manage";

    public static IReadOnlyList<PermissionDefinition> Catalog { get; } =
    [
        new(MembersInvite, [TenantType.Organization]),
        new(MembersManage, [TenantType.Organization]),
        new(MembersRead, [TenantType.Organization]),
        new(PlatformAdminsManage, [TenantType.Platform]),
        new(PlatformAdminsRead, [TenantType.Platform]),
        new(RolesManage, [TenantType.Organization, TenantType.Platform]),
        new(RolesRead, [TenantType.Organization, TenantType.Platform]),
        new(TenantManage, [TenantType.Organization, TenantType.Platform]),
        new(TenantRead, [TenantType.Organization, TenantType.Platform])
    ];
}

public sealed record PermissionDefinition(string Code, IReadOnlyCollection<TenantType> AllowedTenantTypes);
