using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Invitations;

/// <summary>
/// Confirms the identity a Platform invitation was answered by, and does nothing else (IA-REQ-041).
/// <para>
/// The confirmation token is the whole proof, and the offer it belongs to comes from the envelope sealed with it
/// rather than from a second token the caller supplies. That envelope was written in the same transaction as the
/// confirmation, so it is the record of which invitation this is — and it is not something a caller can point
/// somewhere else.
/// </para>
/// <para>
/// No membership is created here. Activation waits for TOTP enrollment, acknowledged recovery codes and an
/// MFA-authenticated session, so this handler deliberately has no access to memberships or roles at all.
/// </para>
/// </summary>
public sealed class ConfirmPlatformInviteeCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IConfirmationSecretStore secrets,
    ITokenHasher tokenHasher,
    IIdentityAccountService identities,
    TimeProvider timeProvider) : IRequestHandler<ConfirmPlatformInviteeCommand, Result>
{
    public Task<Result> Handle(ConfirmPlatformInviteeCommand request, CancellationToken cancellationToken)
    {
        if (!ConfirmationToken.IsCanonical(request.ConfirmationToken))
        {
            return Task.FromResult(Result.Failure(IdentityAccessErrors.InvalidConfirmation()));
        }

        return transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var secret = await secrets.GetByVersionedHashForUpdateAsync(tokenHasher.Hash(request.ConfirmationToken), ct);
            if (secret is null || !tokenHasher.Verify(request.ConfirmationToken, secret.VersionedHash))
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            if (secret.Status == OutboxSecretStatus.Consumed) return Result.Success();

            var message = await context.OutboxMessages.SingleOrDefaultAsync(item => item.Id == secret.OutboxMessageId, ct);

            // The purpose is established before terminality. A Platform confirmation never borrows the
            // organization-registration conflict merely because its envelope can no longer be spent.
            if (message is null || !string.Equals(message.Type, PlatformInvitationDelivery.ConfirmationMessageType, StringComparison.Ordinal))
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            if (secret.Status is not (OutboxSecretStatus.Pending or OutboxSecretStatus.Delivered))
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            if (secret.ExpiresAt <= now)
            {
                secret.Terminate(OutboxSecretStatus.Expired, "confirmation_expired", now);
                await context.SaveChangesAsync(ct);
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            if (!TryReadEnvelope(message.Payload, out var envelope))
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            // The offer is read from the envelope rather than from a second token the caller supplies: the
            // envelope was written in the same transaction as the confirmation it seals, so it is the record of
            // which invitation this confirmation belongs to. The binding is still checked, only from the side
            // the caller cannot influence.
            var invitationId = PlatformAdminInvitationId.From(envelope.InvitationId);
            var invitation = await context.PlatformAdminInvitations
                .SingleOrDefaultAsync(candidate => candidate.Id == invitationId, ct);
            if (invitation is null || invitation.BoundIdentityId != envelope.IdentityId)
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            if (!invitation.IsPendingAt(now))
            {
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            }

            await identities.ActivateAsync(envelope.IdentityId, ct);
            secret.Consume("confirmation_consumed", now);

            // Tenantless, because the identity holds no Platform membership yet and must not appear to.
            context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(envelope.IdentityId, null, $"platform-confirmation-{secret.Id:N}", now));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private static bool TryReadEnvelope(string payload, out PlatformInvitationDelivery.PlatformConfirmationEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<PlatformInvitationDelivery.PlatformConfirmationEnvelope>(payload);
            if (value is null || value.IdentityId == Guid.Empty || value.InvitationId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
