using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Administrators;

/// <summary>
/// Invites a further Platform administrator (IA-REQ-042). It creates an invitation and nothing else: the new
/// administrator walks the same register, confirm, sign-in and MFA gates the bootstrap owner did.
/// </summary>
[Authorize(Permissions.PlatformAdminsManage, true)]
public sealed record InvitePlatformAdministratorCommand(string Email) : IRequest<Result>, ISensitiveRequest;

public sealed class InvitePlatformAdministratorCommandValidator : AbstractValidator<InvitePlatformAdministratorCommand>
{
    public InvitePlatformAdministratorCommandValidator() =>
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256);
}

/// <summary>
/// Ends a Platform membership. It names the membership rather than the identity, because the membership is the
/// only thing the protected directory exposes and the only thing being ended (IA-REQ-044).
/// </summary>
[Authorize(Permissions.PlatformAdminsManage, true)]
public sealed record RevokePlatformAdministratorCommand(Guid MembershipId) : IRequest<Result>;

public sealed class InvitePlatformAdministratorCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<InvitePlatformAdministratorCommand, Result>
{
    public async Task<Result> Handle(InvitePlatformAdministratorCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken))
        {
            return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        }

        string recipient;
        try
        {
            recipient = PlatformAdminInvitation.Canonicalize(request.Email);
        }
        catch (ArgumentException)
        {
            return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());
        }

        return await transaction.ExecuteAsync(async ct =>
        {
            var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
            if (platform is null) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var existing = await context.PlatformAdminInvitations
                .FirstOrDefaultAsync(invitation => invitation.NormalizedEmail == recipient && invitation.Status == PlatformAdminInvitationStatus.Pending, ct);

            var minted = PlatformInvitationDelivery.Mint(tokens, tokenHasher);
            PlatformAdminInvitation invitation;
            if (existing is not null)
            {
                // One live offer per recipient. Reissuing revives the standing one in place rather than leaving a
                // settled row behind to block a second — the same rule an organization invitation follows.
                if (existing.IsOwner) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());
                await PlatformInvitationDelivery.RetireAsync(context, existing.TokenHash, "administrator_invitation_reissued", now, ct);
                existing.Reissue(minted.Hash, now, now.Add(BootstrapWindow));
                invitation = existing;
            }
            else
            {
                invitation = PlatformAdminInvitation.Issue(platform, recipient, minted.Hash, false, now, now.Add(BootstrapWindow));
                context.PlatformAdminInvitations.Add(invitation);
            }

            PlatformInvitationDelivery.Deliver(context, secretWriter, invitation, minted, now, now.Add(BootstrapWindow));
            context.AuditEvents.Add(AuditEvent.Create(
                platform.Id,
                null,
                "platform.administrator.invited",
                $"platform-admin-invite-{invitation.Id.Value:N}",
                new Dictionary<string, string>
                {
                    ["code"] = "platform.administrator.invited",
                    ["outcome"] = existing is null ? "issued" : "reissued"
                }));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private static readonly TimeSpan BootstrapWindow = TimeSpan.FromDays(7);
}

/// <summary>
/// Ends one Platform membership, unless it is the last active owner.
/// <para>
/// The last-owner rule is the reason this is not simply a status write: a Platform with no owner is one nobody can
/// ever administer again, and there is no higher authority to restore it from (IA-REQ-042).
/// </para>
/// </summary>
public sealed class RevokePlatformAdministratorCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa,
    IPlatformMembershipActivator memberships,
    TimeProvider timeProvider) : IRequestHandler<RevokePlatformAdministratorCommand, Result>
{
    public async Task<Result> Handle(RevokePlatformAdministratorCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken))
        {
            return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        }

        if (request.MembershipId == Guid.Empty) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        return await transaction.ExecuteAsync(async ct =>
        {
            var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
            if (platform is null) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var membershipId = MembershipId.From(request.MembershipId);
            var membership = await context.TenantMemberships
                .SingleOrDefaultAsync(candidate => candidate.Id == membershipId && candidate.TenantId == platform.Id, ct);

            // A membership in another tenant is not a Platform administrator, and answering differently would let
            // this route confirm whether an arbitrary membership id exists elsewhere.
            if (membership is null || membership.Status != MembershipStatus.Active)
            {
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());
            }

            if (await memberships.IsLastActiveOwnerAsync(platform, membership, ct))
            {
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());
            }

            membership.Suspend(platform);
            context.AuditEvents.Add(AuditEvent.Create(
                platform.Id,
                membership.IdentityId,
                "platform.administrator.revoked",
                $"platform-admin-revoke-{membership.Id.Value:N}",
                new Dictionary<string, string>
                {
                    ["code"] = "platform.administrator.revoked",
                    ["outcome"] = "suspended"
                }));

            await context.SaveChangesAsync(ct);
            _ = timeProvider;
            return Result.Success();
        }, cancellationToken);
    }
}

/// <summary>
/// Resolves the Platform tenant the caller is acting as. The tenant comes from the validated session and is then
/// checked to actually be Platform and active — a request that merely claimed a tenant id would be the
/// client-selected context IA-REQ-046 forbids.
/// </summary>
internal static class PlatformContext
{
    internal static async Task<Tenant?> ResolveAsync(IApplicationDbContext context, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId) return null;
        var tenant = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        return tenant is { Type: TenantType.Platform, Status: TenantStatus.Active } ? tenant : null;
    }
}
