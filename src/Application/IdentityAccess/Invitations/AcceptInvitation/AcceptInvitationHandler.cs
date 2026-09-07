using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;

/// <summary>
/// The authenticated half of IA-REQ-016. The permission this request carries is application-scoped: it lets any
/// signed-in identity attempt an acceptance and proves nothing about who they are. Every real gate is therefore
/// here — a token that resolves, an identity whose email is confirmed, and an email that is the recipient's.
/// <para>
/// Without the last of those the invitation would be a bearer credential: whoever intercepted the token could
/// join the organization under their own identity.
/// </para>
/// </summary>
public sealed class AcceptInvitationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IUser user,
    IIdentityAccountService identities,
    ITokenHasher tokenHasher,
    IOfferableRoleReader offerableRoles,
    IInvitationRoleAssigner roleAssigner,
    TimeProvider timeProvider) : IRequestHandler<AcceptInvitationCommand, Result<AcceptedInvitation>>
{
    public async Task<Result<AcceptedInvitation>> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        if (user.Id is not { } identityId || identityId == Guid.Empty)
        {
            return Invalid();
        }

        // IA-REQ-005: an unconfirmed identity may not accept, however valid the token is.
        if (await identities.FindByIdAsync(identityId, cancellationToken) is not { IsActive: true } identity)
        {
            return Invalid();
        }

        try
        {
            return await transaction.ExecuteAsync(ct => AcceptAsync(request, identityId, identity.Email, ct), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin token says another request settled this row first. That is the declared retryable
            // conflict (IA-REQ-035), not an unexpected failure.
            return Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The membership uniqueness index is the arbiter when two acceptances race; the loser is a conflict,
            // never a raw database failure.
            return Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }
    }

    private async Task<Result<AcceptedInvitation>> AcceptAsync(AcceptInvitationCommand request, Guid identityId, string email, CancellationToken cancellationToken)
    {
        var now = InvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
        var invitation = await InvitationDelivery.FindByTokenAsync(context, tokenHasher, request.Token, cancellationToken);

        // A token that never existed, a withdrawn offer and a lapsed one are refused identically: which of them
        // it was would tell a token holder what the organization did after inviting them.
        if (invitation is null || !invitation.IsAddressedTo(email))
        {
            return Invalid();
        }

        if (invitation.Status == InvitationStatus.Accepted)
        {
            if (invitation.AcceptedByIdentityId != identityId)
            {
                return Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
            }

            // A replay by the identity that accepted answers with the membership it already has.
            var settled = await context.TenantMemberships
                .FirstOrDefaultAsync(candidate => candidate.TenantId == invitation.TenantId && candidate.IdentityId == identityId, cancellationToken);
            return settled is null
                ? Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvitationConflict())
                : Result<AcceptedInvitation>.Success(new AcceptedInvitation(invitation.TenantId.Value, settled.Id.Value));
        }

        if (!invitation.IsPendingAt(now))
        {
            return Invalid();
        }

        var tenant = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == invitation.TenantId, cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return Invalid();
        }

        // A membership that already exists refuses the acceptance — unless it is `Revoked`, which is the record of
        // a removal rather than a place in the organization. That row is the one this acceptance returns to life,
        // because the tenant's unique membership index means there can never be a second one (IA-REQ-053).
        var existing = await context.TenantMemberships
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenant.Id && candidate.IdentityId == identityId, cancellationToken);
        if (existing is not null && existing.Status != MembershipStatus.Revoked)
        {
            return Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }

        // The roles are re-resolved rather than trusted from the offer, so a role retired between the offer and
        // its acceptance cannot be assigned. The authority check is the inviter's, made when the offer was
        // established, so it is not repeated here (IA-REQ-047).
        var offered = await offerableRoles.ResolveAsync(tenant.Id, identityId, invitation.Roles.Select(role => role.RoleId.Value).ToArray(), cancellationToken);
        if (offered.Roles.Count != invitation.Roles.Count)
        {
            return Invalid();
        }

        TenantMembership membership;
        if (existing is null)
        {
            membership = TenantMembership.CreateInvited(tenant, identityId);
            context.TenantMemberships.Add(membership);
        }
        else
        {
            // Returning is not the undoing of the removal. The membership comes back through the offer that was
            // made — pending first, then activated by this acceptance — and it comes back with nothing: revoking
            // deleted every assignment it held, so what it holds now is exactly what this offer names.
            membership = existing;
            membership.Reinstate(tenant);
        }

        membership.Activate(tenant);
        roleAssigner.Assign(tenant, membership, offered.Roles);

        invitation.Accept(tenant, identityId, now);
        context.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            identityId,
            "invitation.accepted",
            $"invitation-{invitation.Id.Value:N}",
            new Dictionary<string, string> { ["code"] = "invitation.accepted", ["outcome"] = "accepted" }));
        await context.SaveChangesAsync(cancellationToken);
        return Result<AcceptedInvitation>.Success(new AcceptedInvitation(tenant.Id.Value, membership.Id.Value));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } provider &&
        provider.GetType().GetProperty("SqlState")?.GetValue(provider) as string == "23505";

    private static Result<AcceptedInvitation> Invalid() => Result<AcceptedInvitation>.Failure(IdentityAccessErrors.InvalidInvitation());
}
