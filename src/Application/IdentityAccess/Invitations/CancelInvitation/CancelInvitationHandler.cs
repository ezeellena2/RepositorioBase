using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;

/// <summary>
/// Withdraws a standing offer. The invitation stays as history (IA-REQ-036); what is destroyed is the ability to
/// use it, which is why the undelivered envelope goes with it.
/// </summary>
public sealed class CancelInvitationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IUser user,
    TimeProvider timeProvider) : IRequestHandler<CancelInvitationCommand, Result>
{
    public async Task<Result> Handle(CancelInvitationCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId.IsEmpty || request.InvitationId == Guid.Empty ||
            currentTenant.TenantId is not { } activeTenantId || activeTenantId != request.TenantId ||
            user.Id is not { } actorId || actorId == Guid.Empty)
        {
            return Result.Failure(IdentityAccessErrors.InvalidInvitation());
        }

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var now = InvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
                var invitationId = InvitationId.From(request.InvitationId);
                var invitation = await context.Invitations
                    .FirstOrDefaultAsync(candidate => candidate.Id == invitationId && candidate.TenantId == activeTenantId, ct);

                // An invitation of another tenant is simply not there, which is also all a caller may learn about it.
                if (invitation is null)
                {
                    return Result.Failure(IdentityAccessErrors.InvalidInvitation());
                }

                if (invitation.Status == InvitationStatus.Cancelled)
                {
                    // Withdrawing twice is the caller retrying: it answers the same and records nothing new.
                    return Result.Success();
                }

                if (invitation.Status != InvitationStatus.Pending)
                {
                    return Result.Failure(IdentityAccessErrors.InvitationConflict());
                }

                var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == activeTenantId, ct);
                invitation.Cancel(tenant, now);
                await InvitationDelivery.RetireAsync(context, invitation.TokenHash, "invitation.cancelled", now, ct);
                context.AuditEvents.Add(AuditEvent.Create(
                    activeTenantId,
                    actorId,
                    "invitation.cancelled",
                    $"invitation-{invitation.Id.Value:N}",
                    new Dictionary<string, string> { ["code"] = "invitation.cancelled", ["outcome"] = "cancelled" }));
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A lost optimistic update is the declared retryable conflict, never an unexpected failure.
            return Result.Failure(IdentityAccessErrors.InvitationConflict());
        }
    }
}
