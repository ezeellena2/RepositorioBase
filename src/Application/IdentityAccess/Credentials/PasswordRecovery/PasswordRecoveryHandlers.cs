using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;

public sealed class RequestPasswordRecoveryCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<RequestPasswordRecoveryCommand, Result>
{
    /// <summary>The only recovery message, and the only one whose envelope carries a token.</summary>
    public const string MessageType = "identity.password.recovery.requested";

    /// <summary>How long a mailed reset link lives. A **product default**.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Named only by its identifier: the payload holds no address and no token.</summary>
    public sealed record Envelope(Guid RequestId);

    public async Task<Result> Handle(RequestPasswordRecoveryCommand request, CancellationToken cancellationToken)
    {
        if (request.Email is null || request.Email.Length > 256) return Result.Success();
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return Result.Success();

        return await transaction.ExecuteAsync(async ct =>
        {
            var identity = await identities.FindByEmailAsync(email, ct);

            // The answer does not vary. An address with no account simply has nothing to write, and the caller
            // cannot tell that from the outside.
            if (identity is null) return Result.Success();

            var now = timeProvider.GetUtcNow();
            await SupersedePendingAsync(identity.Id, now, ct);

            var rawToken = tokens.Generate();
            var reset = PasswordResetRequest.Issue(identity.Id, tokenHasher.Of(rawToken), now, Lifetime);
            var outbox = OutboxMessage.Create(MessageType, JsonSerializer.Serialize(new Envelope(reset.Id)), now);
            context.PasswordResetRequests.Add(reset);
            context.OutboxMessages.Add(outbox);
            context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), reset.ExpiresAt));
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identity.Id, null, "identity.password.recovery.requested", AuditCorrelation.Current(), "accepted"));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// Reissuing replaces rather than adds. The previous envelope is terminalized in the same transaction, so the
    /// older link is dead the moment the newer one exists — not merely older than it.
    /// </summary>
    private async Task SupersedePendingAsync(Guid identityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await context.PasswordResetRequests
            .Where(candidate => candidate.IdentityId == identityId && candidate.Status == PasswordResetStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var request in pending)
        {
            request.Supersede(now);
            await TerminalizeEnvelopeAsync(request.Id, "superseded", now, cancellationToken);
        }
    }

    private async Task TerminalizeEnvelopeAsync(Guid requestId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new Envelope(requestId));
        var message = await context.OutboxMessages
            .SingleOrDefaultAsync(candidate => candidate.Type == MessageType && candidate.Payload == payload, cancellationToken);
        if (message is null) return;

        var secret = await context.OutboxSecrets.SingleOrDefaultAsync(candidate => candidate.OutboxMessageId == message.Id, cancellationToken);
        if (secret is null || secret.Status is OutboxSecretStatus.Consumed or OutboxSecretStatus.Failed or OutboxSecretStatus.Expired) return;
        secret.Terminate(OutboxSecretStatus.Failed, reason, now);
    }
}

public sealed class ResetPasswordCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityCredentialService credentials,
    IRecentIdentityProofStore proofs,
    ITokenHasher tokenHasher,
    TimeProvider timeProvider) : IRequestHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        if (request.Token is null || request.NewPassword is null || request.NewPassword.Length > 256)
            return Result.Failure(IdentityAccessErrors.InvalidCredentialToken());

        var hash = tokenHasher.Of(request.Token);
        if (hash.IsEmpty) return Result.Failure(IdentityAccessErrors.InvalidCredentialToken());

        return await transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var reset = await context.PasswordResetRequests.SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, ct);

            // Unknown, spent, superseded and expired are one answer. Which of them it was is state the holder of a
            // dead link was never shown.
            if (reset is null || !reset.IsPendingAt(now)) return Result.Failure(IdentityAccessErrors.InvalidCredentialToken());

            var applied = await credentials.ReplacePasswordAsync(reset.IdentityId, request.NewPassword, ct);
            if (!applied.Succeeded) return Result.Failure(IdentityAccessErrors.PasswordPolicyFailed(applied.Errors));

            reset.Consume(now);
            await proofs.RecordPasswordChangeAsync(reset.IdentityId, ct);
            await CredentialSessionEffects.RevokeEveryLiveSessionAsync(context, reset.IdentityId, null, now, "password_reset", ct);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(reset.IdentityId, null, "identity.password.reset", AuditCorrelation.Current(), "reset"));
            await context.SaveChangesAsync(ct);

            // Deliberately no session. Holding a mailed link is not the same as having signed in, and issuing one
            // here would turn a link somebody else could have intercepted into an authenticated session.
            return Result.Success();
        }, cancellationToken);
    }
}
