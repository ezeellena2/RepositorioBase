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

    /// <summary>
    /// A person's own profile and their own `Personal` context. Both are application-scoped self-service: the
    /// requests carry no owner parameter and resolve the caller's own rows, and creating the context is what
    /// produces the tenant, so requiring one would make it unreachable (IA-REQ-050).
    /// </summary>
    public const string IdentityProfileRead = "identity.profile.read";

    public const string IdentityProfileManage = "identity.profile.manage";

    /// <summary>
    /// Proving it is still you, and changing what you prove it with. Application-scoped for the same reason the
    /// rest of self-service is: the request carries no subject and resolves the caller's own credentials.
    /// </summary>
    public const string IdentityCredentialsManage = "identity.credentials.manage";

    /// <summary>Linking and unlinking a person's own provider accounts. Self-service for the same reason.</summary>
    public const string IdentityExternalManage = "identity.external.manage";

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
        PlatformMfaEnroll,
        IdentityProfileRead,
        IdentityProfileManage,
        IdentityCredentialsManage,
        IdentityExternalManage
    };

    /// <summary>
    /// The application-scoped capabilities an authenticated identity simply has, rather than ones granted to it.
    /// <para>
    /// They are declared here rather than restated by the evaluator because the two lists drifting apart is not
    /// a visible failure: a capability missing from the evaluator's copy falls through to a persisted claim
    /// lookup that finds nothing, and the caller is told they lack a permission nobody was ever meant to grant.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> SelfServiceCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        IdentitySessionManage,
        IdentityContextRead,
        IdentityContextSelect,
        IdentityInvitationsAccept,
        PlatformMfaEnroll,
        IdentityProfileRead,
        IdentityProfileManage,
        IdentityCredentialsManage,
        IdentityExternalManage
    };

}

public sealed record PermissionDefinition(string Code, IReadOnlyCollection<TenantType> AllowedTenantTypes);
