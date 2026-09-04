using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;

/// <summary>
/// Re-offers a standing invitation under a new token. It is a rotation, not a second send: the previous token and
/// its envelope stop being usable in the same transaction, so there is never an instant with two live tokens for
/// one invitation (IA-REQ-015/017).
/// <para>
/// A resend establishes an offer, so IA-REQ-047 binds it as it binds the first one. An inviter who has since lost
/// the authority to make this offer cannot keep it alive by renewing it.
/// </para>
/// </summary>
public sealed class ResendInvitationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IUser user,
    IOfferableRoleReader offerableRoles,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<ResendInvitationCommand, Result>
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public async Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } activeTenantId || activeTenantId != request.TenantId ||
            user.Id is not { } actorId || actorId == Guid.Empty)
        {
            return Result.Failure(IdentityAccessErrors.InvalidInvitation());
        }

        return await transaction.ExecuteAsync(async ct =>
        {
            var now = InvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
            var invitationId = InvitationId.From(request.InvitationId);
            var invitation = await context.Invitations
                .Include(candidate => candidate.Roles)
                .FirstOrDefaultAsync(candidate => candidate.Id == invitationId && candidate.TenantId == activeTenantId, ct);

            if (invitation is null)
            {
                return Result.Failure(IdentityAccessErrors.InvalidInvitation());
            }

            if (invitation.Status != InvitationStatus.Pending)
            {
                // A settled offer is not revivable: reviving it would resurrect a decision the tenant already made.
                return Result.Failure(IdentityAccessErrors.InvitationConflict());
            }

            var offer = await offerableRoles.ResolveAsync(
                activeTenantId,
                actorId,
                invitation.Roles.Select(role => role.RoleId.Value).ToArray(),
                ct);
            if (!offer.IsOfferable)
            {
                return Result.Failure(IdentityAccessErrors.InvalidInvitation());
            }

            var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == activeTenantId, ct);
            var superseded = invitation.TokenHash;
            var expiresAt = InvitationDelivery.ToStorablePrecision(now.Add(Window));
            var minted = InvitationDelivery.Mint(tokens, tokenHasher);
            InvitationDelivery.Deliver(context, secretWriter, minted, invitation.Id, activeTenantId, now, expiresAt);

            invitation.Reissue(tenant, minted.Hash, now, expiresAt);
            await InvitationDelivery.RetireAsync(context, superseded, "invitation.superseded", now, ct);
            context.AuditEvents.Add(AuditEvent.Create(
                activeTenantId,
                actorId,
                "invitation.issued",
                $"invitation-{invitation.Id.Value:N}",
                new Dictionary<string, string> { ["code"] = "invitation.issued", ["outcome"] = "resent" }));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}
