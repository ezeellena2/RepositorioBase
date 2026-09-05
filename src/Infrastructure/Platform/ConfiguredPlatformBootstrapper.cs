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
/// The budget is five attempts per fifteen minutes. The SPEC fixes no number, so this is a choice: enough that an
/// operator retrying a failed delivery is not blocked, few enough that the endpoint cannot be used to hammer the
/// outbox. It is in-process, which is the right scope for a ceremony that runs at most a handful of times in a
/// deployment's life; a distributed limiter would be worth it only if this endpoint were hot, and it never is.
/// </para>
/// </summary>
public sealed class PlatformBootstrapRecoveryRateLimiter(IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
    : IPlatformBootstrapRecoveryRateLimiter
{
    internal const int Budget = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> Attempts = new(StringComparer.Ordinal);

    public Task<PlatformRecoveryLease> TryAcquireAsync(Guid? pendingInvitationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var source = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{pendingInvitationId?.ToString("N") ?? "none"}|{source}";
        var attempts = Attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        lock (attempts)
        {
            while (attempts.Count > 0 && now - attempts.Peek() > Window)
            {
                attempts.Dequeue();
            }

            if (attempts.Count >= Budget)
            {
                var retryAfter = (int)Math.Ceiling((Window - (now - attempts.Peek())).TotalSeconds);
                return Task.FromResult(new PlatformRecoveryLease(false, Math.Max(retryAfter, 1)));
            }

            attempts.Enqueue(now);
            return Task.FromResult(PlatformRecoveryLease.Granted);
        }
    }

    /// <summary>
    /// Clears the in-process budget. It is public because the budget outlives a test — it is process state, not
    /// database state — and a suite where one test inherits another's attempts is one that fails by order.
    /// </summary>
    public static void Reset() => Attempts.Clear();
}
