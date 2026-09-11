using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;

/// <summary>
/// The one thing a deployment may decide about Platform: which address receives the first owner invitation.
/// <para>
/// It is deliberately only that. Configuration is a selector at the moment the tenant is absent and never an
/// authority afterwards — changing this value later must not replace or elevate anybody (IA-REQ-040).
/// </para>
/// </summary>
public interface IPlatformBootstrapOptions
{
    string? OwnerEmail { get; }
}

/// <summary>The ceremony's input. It carries nothing: everything it needs is configuration and current state.</summary>
public sealed record BootstrapPlatformOwnerCommand;

/// <summary>Creates the Platform system roles and grants the owner role its permissions.</summary>
public interface IPlatformSystemRoleProvisioner
{
    Task ProvisionAsync(Tenant platform, CancellationToken cancellationToken);
}

/// <summary>
/// Creates the Platform tenant, its system roles, and one pending owner invitation — once, and only while the
/// tenant is absent (IA-REQ-040).
/// <para>
/// It is not a request anybody can send. Bootstrap runs from the host as it starts, because a route that created
/// the Platform tenant would be a route that created the system's highest authority from outside it. Being
/// idempotent is what makes running it on every start safe: a second invocation finds the tenant and does nothing.
/// </para>
/// <para>
/// Nothing here creates an identity or a password. The invitation is the whole output, and the recipient still has
/// to register, confirm, sign in and complete MFA before any of it becomes authority.
/// </para>
/// </summary>
public sealed class BootstrapPlatformOwner(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IPlatformBootstrapOptions options,
    IPlatformSystemRoleProvisioner roles,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    LocalizationSettings localization,
    TimeProvider timeProvider)
{
    internal static readonly TimeSpan InvitationWindow = TimeSpan.FromDays(7);

    /// <summary>Answers whether this invocation was the one that created it.</summary>
    public async Task<bool> ExecuteAsync(BootstrapPlatformOwnerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A deployment that configured nothing gets nothing. Inventing a recipient would be inventing an owner.
        if (string.IsNullOrWhiteSpace(options.OwnerEmail)) return false;

        string recipient;
        try
        {
            recipient = PlatformAdminInvitation.Canonicalize(options.OwnerEmail);
        }
        catch (ArgumentException)
        {
            // A misconfigured address is a deployment error, not a reason to create a Platform with no owner.
            return false;
        }

        if (await context.Tenants.AnyAsync(tenant => tenant.Type == TenantType.Platform, cancellationToken))
        {
            return false;
        }

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
                var platform = Tenant.CreatePlatform();
                platform.Activate();
                context.Tenants.Add(platform);
                await roles.ProvisionAsync(platform, ct);

                var minted = PlatformInvitationDelivery.Mint(tokens, tokenHasher);
                var invitation = PlatformAdminInvitation.Issue(
                    platform,
                    recipient,
                    minted.Hash,
                    true,
                    localization.DefaultLanguage,
                    now,
                    now.Add(InvitationWindow));
                context.PlatformAdminInvitations.Add(invitation);
                PlatformInvitationDelivery.Deliver(context, secretWriter, invitation, minted, now, now.Add(InvitationWindow));

                context.AuditEvents.Add(AuditEvent.Create(
                    platform.Id,
                    null,
                    "platform.bootstrap.completed",
                    $"platform-bootstrap-{invitation.Id.Value:N}",
                    new Dictionary<string, string>
                    {
                        ["code"] = "platform.bootstrap.completed",
                        ["outcome"] = "owner_invited"
                    }));

                await context.SaveChangesAsync(ct);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two hosts starting at once: the unique Platform slug lets exactly one of them win, and the loser has
            // nothing to do. Which one won is not interesting; that there is one is.
            return false;
        }
    }

}
