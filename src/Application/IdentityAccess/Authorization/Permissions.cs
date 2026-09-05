using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

public static class Permissions
{
    public const string ApplicationPermissionClaimType = "permission";
    public const string TodosRead = "todos.read";
    public const string TodosWrite = "todos.write";
    public const string WeatherRead = "weather.read";
    public const string MembersRead = "members.read";
    public const string MembersManage = "members.manage";
    public const string MembersInvite = "members.invite";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string TenantRead = "tenant.read";
    public const string TenantManage = "tenant.manage";
    public const string IdentitySessionManage = "identity.sessions.manage";
    public const string IdentityContextRead = "identity.context.read";
    public const string IdentityContextSelect = "identity.context.select";
    public const string IdentityInvitationsAccept = "identity.invitations.accept";
    public const string PlatformAdminsRead = "platform.admins.read";
    public const string PlatformAdminsManage = "platform.admins.manage";

    /// <summary>
    /// The organization lifecycle Platform may drive. It reads as `tenants` while the matching read code reads
    /// as `organizations` because the SPEC route table names them that way, and the routes are the contract.
    /// </summary>
    public const string PlatformTenantsManage = "platform.tenants.manage";
    public const string PlatformOrganizationsRead = "platform.organizations.read";
    public const string PlatformIdentitiesRead = "platform.identities.read";
    public const string PlatformAuditRead = "platform.audit.read";

    /// <summary>
    /// Enrolling and stepping up the Platform second factor. The SPEC names no code for it, so this one is
    /// chosen to fit the existing `resource.action` catalogue. It is application-scoped for the same reason
    /// accepting an invitation is: the invitee holds no membership until the gates complete, so there is no
    /// tenant to scope it to, and requiring one would make the gates unreachable.
    /// </summary>
    public const string PlatformMfaEnroll = "platform.mfa.enroll";

    public static IReadOnlyList<PermissionDefinition> Catalog { get; } =
    [
        new(MembersInvite, [TenantType.Organization]),
        new(MembersManage, [TenantType.Organization]),
        new(MembersRead, [TenantType.Organization]),
        new(PlatformAdminsManage, [TenantType.Platform]),
        new(PlatformAdminsRead, [TenantType.Platform]),
        new(PlatformAuditRead, [TenantType.Platform]),
        new(PlatformIdentitiesRead, [TenantType.Platform]),
        new(PlatformOrganizationsRead, [TenantType.Platform]),
        new(PlatformTenantsManage, [TenantType.Platform]),
        new(RolesManage, [TenantType.Organization, TenantType.Platform]),
        new(RolesRead, [TenantType.Organization, TenantType.Platform]),
        new(TenantManage, [TenantType.Organization, TenantType.Platform]),
        new(TenantRead, [TenantType.Organization, TenantType.Platform])
    ];

    public static IReadOnlySet<string> ApplicationScopedCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TodosRead,
        TodosWrite,
        WeatherRead,
        IdentitySessionManage,
        IdentityContextRead,
        IdentityContextSelect,
        // An invitee holds no membership until acceptance succeeds, so accepting cannot be tenant-scoped. It is a
        // self-service capability of the authenticated identity, like reading its own context.
        IdentityInvitationsAccept,
        // A Platform invitee holds no membership until the MFA gates complete, so enrolling cannot be
        // tenant-scoped either. It grants nothing beyond the chance to prove a factor.
        PlatformMfaEnroll
    };

}

public sealed record PermissionDefinition(string Code, IReadOnlyCollection<TenantType> AllowedTenantTypes);
