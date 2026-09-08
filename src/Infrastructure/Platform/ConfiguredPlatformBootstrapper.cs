using System.Collections.Concurrent;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Infrastructure.Platform;

/// <summary>
/// Reads the one value a deployment may configure about Platform (IA-REQ-040).
/// <para>
/// The key is <c>IdentityAccess:Platform:BootstrapOwnerEmail</c>. The SPEC does not name one, so this is a choice;
/// it sits under the same section as the rest of the Identity Access configuration so an operator finds it where
/// they find the others.
/// </para>
/// </summary>
public sealed class ConfiguredPlatformBootstrapper(IConfiguration configuration) : IPlatformBootstrapOptions
{
    internal const string OwnerEmailKey = "IdentityAccess:Platform:BootstrapOwnerEmail";

    public string? OwnerEmail => configuration[OwnerEmailKey];
}

/// <summary>
/// Creates the two Platform system roles and grants each one its permissions.
/// <para>
/// An administrator may read every directory and drive the Organization lifecycle; only an owner may invite or
/// revoke another administrator. That split is what makes the owner a distinct thing rather than a label:
/// operating Platform and deciding who operates it are different authorities (IA-REQ-042).
/// </para>
/// <para>
/// The grants are fixed here because nothing in this increment edits a Platform role. An administrator with no
/// permissions at all would be an account that can sign in, complete MFA, and then do nothing — and there would
/// be no route to fix it.
/// </para>
/// </summary>
public sealed class PlatformSystemRoleProvisioner(ApplicationDbContext context) : IPlatformSystemRoleProvisioner
{
    /// <summary>What operating Platform means: read every directory, and drive the Organization lifecycle.</summary>
    private static readonly string[] AdministratorPermissions =
    [
        Permissions.PlatformAdminsRead,
        Permissions.PlatformOrganizationsRead,
        Permissions.PlatformIdentitiesRead,
        // Stopping an abusive account is operating Platform, the same way suspending an organization is. Deciding
        // who the operators are stays with the owner alone (IA-REQ-042, IA-REQ-054).
        Permissions.PlatformIdentitiesManage,
        // Retention is an operating concern: reading what the deployment's rules are, and stopping an erasure
        // while something is being looked into (IA-REQ-056).
        Permissions.PlatformRetentionRead,
        Permissions.PlatformRetentionManage,
        // Resolving a documentary dispute is an operating action, and the only one that can write a document
        // value — against a stored dispute, never for the operator's own identity (IA-REQ-058).
        Permissions.PlatformDocumentsResolve,
        Permissions.PlatformAuditRead,
        Permissions.PlatformTenantsManage
    ];

    /// <summary>Everything an administrator may do, plus deciding who the administrators are.</summary>
    private static readonly string[] OwnerPermissions =
        [.. AdministratorPermissions, Permissions.PlatformAdminsManage];

    public async Task ProvisionAsync(Tenant platform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platform);

        var owner = Role.CreateSystem(platform, PlatformRoles.Owner);
        var administrator = Role.CreateSystem(platform, PlatformRoles.Administrator);
        context.TenantRoles.AddRange(owner, administrator);

        await GrantAsync(platform, owner, OwnerPermissions, cancellationToken);
        await GrantAsync(platform, administrator, AdministratorPermissions, cancellationToken);
    }

    private async Task GrantAsync(Tenant platform, Role role, string[] codes, CancellationToken cancellationToken)
    {
        foreach (var code in codes)
        {
            var permission = await context.Permissions.SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
            if (permission is null)
            {
                // The catalogue is synchronized as the application starts. A missing code here would mean the
                // ceremony ran before it, and granting nothing is safer than granting a permission that does not
                // exist — bootstrap can run again on the next start.
                throw new InvalidOperationException($"The permission catalogue does not contain {code}.");
            }

            context.RolePermissions.Add(RolePermission.Create(platform, role, permission));
        }
    }
}

/// <summary>
/// Bounds how often bootstrap recovery may run (IA-REQ-040).
/// <para>
/// The key is the pending invitation plus the request's transport source, both derived here. Nothing the caller
/// supplies takes part: the request is bodyless precisely so there is nothing to vary, and a limiter keyed on
/// caller input would be one an attacker re-keys per attempt.
/// </para>
/// <para>
/// The budget lives in the shared store, so it is one budget for the deployment rather than one per process. It
/// used to be a static dictionary, which meant a restart refunded every attempt and two instances doubled it —
/// invisible from inside one process, and never something a caller had to earn (IA-REQ-057).
/// </para>
/// </summary>
public sealed class PlatformBootstrapRecoveryRateLimiter(IHttpContextAccessor httpContextAccessor, ISharedAttemptBudget budgets)
    : IPlatformBootstrapRecoveryRateLimiter
{
    /// <summary>Hosts that report no address share one key rather than each escaping the budget.</summary>
    private const string UnknownSource = "unknown";

    public Task<AttemptBudgetDecision> TryAcquireAsync(Guid? pendingInvitationId, CancellationToken cancellationToken)
    {
        var source = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? UnknownSource;
        var key = $"{pendingInvitationId?.ToString("N") ?? "none"}|{source}";
        return budgets.SpendAsync(CleanArchitecture.Application.IdentityAccess.Platform.PlatformAttemptBudgets.BootstrapRecovery, key, cancellationToken);
    }
}
