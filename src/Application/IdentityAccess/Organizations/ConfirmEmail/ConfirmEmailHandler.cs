using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;

public sealed class ConfirmEmailCommandHandler(IApplicationTransaction transaction, IApplicationDbContext context, IConfirmationSecretStore secrets, ITokenHasher tokenHasher, IIdentityAccountService identities, TimeProvider timeProvider) : IRequestHandler<ConfirmEmailCommand, Result>
{
    private const string ConfirmationMessageType = "identity.confirmation.requested";

    public Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        if (!ConfirmationToken.IsCanonical(request.Token)) return Task.FromResult(Result.Failure(IdentityAccessErrors.InvalidConfirmation()));
        return transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var hash = tokenHasher.Hash(request.Token);
            var secret = await secrets.GetByVersionedHashForUpdateAsync(hash, ct);
            if (secret is null || !tokenHasher.Verify(request.Token, secret.VersionedHash)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            if (secret.Status == OutboxSecretStatus.Consumed) return Result.Success();
            if (secret.Status is not (OutboxSecretStatus.Pending or OutboxSecretStatus.Delivered)) return Result.Failure(IdentityAccessErrors.RegistrationConflict());
            if (secret.ExpiresAt <= now)
            {
                secret.Terminate(OutboxSecretStatus.Expired, "confirmation_expired", now);
                await context.SaveChangesAsync(ct);
                return Result.Failure(IdentityAccessErrors.RegistrationConflict());
            }

            var message = await context.OutboxMessages.SingleOrDefaultAsync(item => item.Id == secret.OutboxMessageId, ct);
            if (message is null || !string.Equals(message.Type, ConfirmationMessageType, StringComparison.Ordinal)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            if (!TryReadEnvelope(message.Payload, out var envelope)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.Id == TenantId.From(envelope.TenantId), ct);
            var membership = await context.TenantMemberships.SingleOrDefaultAsync(item => item.Id == MembershipId.From(envelope.MembershipId), ct);
            if (tenant is null || membership is null || membership.TenantId != tenant.Id || membership.IdentityId != envelope.IdentityId)
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            if (tenant.Status != TenantStatus.PendingConfirmation || membership.Status != MembershipStatus.PendingConfirmation)
                return Result.Failure(IdentityAccessErrors.RegistrationConflict());

            await identities.ActivateAsync(envelope.IdentityId, ct);
            tenant.Activate();
            membership.Activate(tenant);
            secret.Consume("confirmation_consumed", now);
            context.AuditEvents.Add(AuditEvent.Create(tenant.Id, envelope.IdentityId, "identity.confirmed", $"confirmation-{secret.Id:N}", new Dictionary<string, string> { ["code"] = "identity.confirmed", ["outcome"] = "activated" }));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private static bool TryReadEnvelope(string payload, out ConfirmationEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload);
            if (value is null || value.IdentityId == Guid.Empty || value.TenantId == Guid.Empty || value.MembershipId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid TenantId, Guid MembershipId);
}
