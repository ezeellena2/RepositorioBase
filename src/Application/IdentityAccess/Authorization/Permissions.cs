using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Authorization;

public static class Permissions
{
    public const string ApplicationPermissionClaimType = "permission";
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

    /// <summary>
    /// Stopping and restarting a person's account (IA-REQ-054). Distinct from `platform.identities.read`, which
    /// only shows the directory, and from `platform.tenants.manage`, which drives organizations: an operator who
    /// may suspend a company is not thereby entitled to suspend a person.
    /// </summary>
    public const string PlatformIdentitiesManage = "platform.identities.manage";

    /// <summary>
    /// Resolving somebody's documentary dispute (IA-REQ-058). Distinct from every other Platform code, because
    /// it is the only one that can cause a document value to be written at all — and it can do so only against a
    /// stored dispute, and never for the operator's own identity.
    /// </summary>
    public const string PlatformDocumentsResolve = "platform.identities.documents.resolve";
    public const string PlatformAuditRead = "platform.audit.read";

    /// <summary>
    /// Reading the retention policy, and placing or releasing a legal hold (IA-REQ-056). Two codes rather than
    /// one, and manage deliberately does not imply read: stopping an erasure and reading what the deployment's
    /// retention rules are are different things to be trusted with.
    /// </summary>
    public const string PlatformRetentionRead = "platform.retention.read";

    public const string PlatformRetentionManage = "platform.retention.manage";

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

    /// <summary>
    /// Parking your own account (IA-REQ-054). Application-scoped self-service like the rest: the request carries
    /// no subject and resolves the caller's own identity. Deliberately not `identity.credentials.manage` —
    /// changing what you prove with and stopping the account you prove into are different powers, and the second
    /// is the one that can leave an organization with nobody to run it.
    /// </summary>
    public const string IdentityAccountManage = "identity.account.manage";

    /// <summary>
    /// Opening a dispute over your own recorded document (IA-REQ-058). Application-scoped self-service: the
    /// request carries no subject and resolves the caller's own document. It grants no power to write one.
    /// </summary>
    public const string IdentityDocumentDispute = "identity.document.dispute";

    /// <summary>
    /// Handing an `Organization` to somebody else. Deliberately not `tenant.manage`: managing a tenant and giving
    /// it away are different powers, and one administrator holding the first must not thereby hold the second
    /// (IA-REQ-053). Necessary but never sufficient — the actor must also be the current owner.
    /// </summary>
    public const string TenantOwnershipTransfer = "tenant.ownership.transfer";

    /// <summary>
    /// Every permission the backend defines, and for each one the answer to "does an `Organization`'s system
    /// `Owner` role hold it?"
    /// <para>
    /// That third argument is positional on purpose (amendment D1). C5's grant ceiling says an actor may grant
    /// only what it holds, which makes the answer load-bearing: a code no owner holds is a code no administrator
    /// in any organization can ever grant. Making it automatic — "every future addition goes to `Owner`" — would
    /// widen every existing owner's authority as a side effect of shipping an unrelated feature. Making it a
    /// parameter means adding a permission does not compile until somebody has answered.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PermissionDefinition> Catalog { get; } =
    [
        new(MembersInvite, [TenantType.Organization], OrganizationOwner.Holds),
        new(MembersManage, [TenantType.Organization], OrganizationOwner.Holds),
        new(MembersRead, [TenantType.Organization], OrganizationOwner.Holds),
        // Platform's roles are provisioned by its own bootstrap and reach no Organization owner.
        new(PlatformAdminsManage, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformAdminsRead, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformAuditRead, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformDocumentsResolve, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformIdentitiesManage, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformIdentitiesRead, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformOrganizationsRead, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformRetentionManage, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformRetentionRead, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(PlatformTenantsManage, [TenantType.Platform], OrganizationOwner.NotApplicable),
        new(RolesManage, [TenantType.Organization, TenantType.Platform], OrganizationOwner.Holds),
        new(RolesRead, [TenantType.Organization, TenantType.Platform], OrganizationOwner.Holds),
        new(TenantManage, [TenantType.Organization, TenantType.Platform], OrganizationOwner.Holds),
        new(TenantOwnershipTransfer, [TenantType.Organization], OrganizationOwner.Holds),
        new(TenantRead, [TenantType.Organization, TenantType.Platform], OrganizationOwner.Holds)
    ];

    /// <summary>
    /// What the system `Owner` role of an `Organization` is provisioned with, derived from the one place the
    /// answer is recorded rather than restated as a second list that could drift from it.
    /// </summary>
    public static IReadOnlyList<string> OrganizationOwnerCodes { get; } =
        Catalog.Where(definition => definition.OrganizationOwner == OrganizationOwner.Holds)
            .Select(definition => definition.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlySet<string> ApplicationScopedCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
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
        IdentityExternalManage,
        IdentityAccountManage,
        IdentityDocumentDispute
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
        IdentityExternalManage,
        IdentityAccountManage,
        IdentityDocumentDispute
    };

}

/// <summary>Whether an `Organization`'s system `Owner` role is provisioned with a permission (amendment D1).</summary>
public enum OrganizationOwner
{
    /// <summary>The owner holds it, for existing organizations by backfill and for new ones at provisioning.</summary>
    Holds,

    /// <summary>
    /// Deliberately withheld from the owner although an `Organization` may hold it — so it can only reach anybody
    /// through a role somebody who already holds it grants, which under C5's ceiling means nobody, until this
    /// answer changes. Recorded rather than omitted, so the withholding is visible.
    /// </summary>
    Withheld,

    /// <summary>The catalogue does not allow it to an `Organization` at all, so the question does not arise.</summary>
    NotApplicable
}

public sealed record PermissionDefinition(
    string Code,
    IReadOnlyCollection<TenantType> AllowedTenantTypes,
    OrganizationOwner OrganizationOwner);
