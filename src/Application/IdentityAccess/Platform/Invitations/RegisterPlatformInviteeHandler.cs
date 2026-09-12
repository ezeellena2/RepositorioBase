using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Invitations;

/// <summary>
/// The public half of Platform onboarding (IA-REQ-041). It gets a recipient as far as being confirmable and no
/// further: it never creates a Platform membership, and it never grants anything.
/// <para>
/// Every outcome after the password gate is the same neutral success, because the caller must not be able to
/// learn whether the token is live, or — given a live one — whether the address already has an account. The
/// password gate comes first precisely so that it reads no state: it answers identically in all four
/// combinations, so it cannot serve as an oracle for either question.
/// </para>
/// <para>
/// The two branches differ only in what they write. A missing identity is created with the submitted password
/// through the configured policy — the system never generates one. An existing identity has its credential
/// material ignored entirely: holding this token is not proof of owning that account, so nothing about it is
/// changed and its owner is told, generically, that they can sign in.
/// </para>
/// </summary>
public sealed class RegisterPlatformInviteeCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    LocalizationSettings localization,
    TimeProvider timeProvider) : IRequestHandler<RegisterPlatformInviteeCommand, Result>
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterPlatformInviteeCommand request, CancellationToken cancellationToken)
    {
        var passwordValidation = await identities.ValidatePasswordAsync(request.Password, cancellationToken);
        if (!passwordValidation.IsValid)
        {
            return Result.Failure(IdentityAccessErrors.PasswordPolicyFailed(
                new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal)
                {
                    ["password"] = [new ValidationErrorDetail(ValidationErrorCodes.PasswordPolicy, new Dictionary<string, int>())]
                }));
        }

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var now = PlatformInvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
                var invitation = await PlatformInvitationDelivery.FindByTokenAsync(context, tokenHasher, request.Token, ct);

                // A token that resolves to nothing, one whose window has closed and one that was withdrawn are
                // all equally unusable, and they answer exactly as a live one does.
                if (invitation is null || !invitation.IsPendingAt(now))
                {
                    return Result.Success();
                }

                if (await identities.FindByEmailAsync(invitation.NormalizedEmail, ct) is { } existing)
                {
                    // Binding is safe here and only here: the identity comes from the invitation's own recipient,
                    // never from anything the caller supplied, so an anonymous request cannot point a standing
                    // Platform offer at an account of its choosing.
                    invitation.Bind(existing.Id, now);

                    // An identity that exists is not the same thing as an identity that is usable, and treating
                    // them alike is what made an expired confirmation a dead end: this flow creates an unconfirmed
                    // identity on purpose, and only the create path ever minted a confirmation, which by
                    // definition cannot run twice for one address. A pending one is therefore sent another
                    // confirmation and nothing else — its credential is untouched, its recipient binding is the
                    // invitation's own, and no membership is created (IA-REQ-041).
                    //
                    // The branch names the state rather than asking "not active", because C6 added states that
                    // are also not active and mean something else entirely: an account somebody parked, or one an
                    // operator suspended, must not be handed a fresh confirmation as though it had never
                    // confirmed its address (IA-REQ-054).
                    if (existing.Status == CleanArchitecture.Domain.IdentityAccess.Identities.IdentityAccountStatus.PendingConfirmation)
                    {
                        await ReissueConfirmationAsync(existing.Id, invitation.Id.Value, now, ct);
                    }
                    else
                    {
                        context.OutboxMessages.Add(OutboxMessage.Create(
                            PlatformInvitationDelivery.ExistingIdentityNoticeMessageType,
                            JsonSerializer.Serialize(new PlatformInvitationDelivery.PlatformConfirmationEnvelope(existing.Id, invitation.Id.Value)),
                            now));
                    }

                    await context.SaveChangesAsync(ct);
                    return Result.Success();
                }

                var creation = await identities.CreatePendingAsync(
                    invitation.NormalizedEmail,
                    request.Password,
                    invitation.Language ?? localization.DefaultLanguage,
                    ct);
                if (creation.Account is null)
                {
                    // An address the aggregate accepts and ASP.NET Identity does not, or a competing request that
                    // created it between the lookup and this call. Failing here would answer 400 for a real token
                    // while an unknown one answers 202, which is the token oracle this flow exists to prevent.
                    return Result.Success();
                }

                invitation.Bind(creation.Account.Id, now);
                var rawToken = tokens.Generate();
                var outbox = OutboxMessage.Create(
                    PlatformInvitationDelivery.ConfirmationMessageType,
                    JsonSerializer.Serialize(new PlatformInvitationDelivery.PlatformConfirmationEnvelope(creation.Account.Id, invitation.Id.Value)),
                    now);
                context.OutboxMessages.Add(outbox);
                context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.Add(ConfirmationWindow)));
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A competing request created the identity between the lookup and the create. The address exists
            // after all, which is the existing-identity branch, and that branch is neutral.
            return Result.Success();
        }
    }

    /// <summary>
    /// Mints a fresh confirmation for an identity that never completed one, and retires whatever earlier envelope
    /// was still open for the same identity and invitation.
    /// <para>
    /// Retiring first is what keeps "one usable confirmation" true: leaving the old envelope readable would mean
    /// two live tokens for one address, and a token that stops resolving is exactly what a superseded envelope is.
    /// The password the caller submitted takes no part — the account already exists, and holding this invitation
    /// is not proof of owning it.
    /// </para>
    /// </summary>
    private async Task ReissueConfirmationAsync(Guid identityId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Bounded by the confirmation window rather than scanning every confirmation this deployment ever wrote:
        // an older envelope is already past its expiry and refused on its own, and this route is public, so an
        // unbounded read of a table with no index on Type would be work a caller could ask for at will.
        var oldest = now.Add(-ConfirmationWindow);
        var priorMessages = await context.OutboxMessages
            .Where(message => message.Type == PlatformInvitationDelivery.ConfirmationMessageType && message.CreatedAt >= oldest)
            .Select(message => new { message.Id, message.Payload })
            .ToListAsync(cancellationToken);
        var superseded = priorMessages
            .Where(message => Names(message.Payload, identityId, invitationId))
            .Select(message => message.Id)
            .ToHashSet();
        if (superseded.Count > 0)
        {
            // Both states confirmation accepts, not only Pending. A delivered-but-unspent envelope is exactly the
            // one the recipient still holds a link for, so leaving it open would be two usable confirmations for
            // one address — which is the thing this method claims not to allow.
            var open = await context.OutboxSecrets
                .Where(secret => superseded.Contains(secret.OutboxMessageId) &&
                                 (secret.Status == OutboxSecretStatus.Pending || secret.Status == OutboxSecretStatus.Delivered))
                .ToListAsync(cancellationToken);
            foreach (var secret in open) secret.Terminate(OutboxSecretStatus.Expired, "confirmation_reissued", now);
        }

        var rawToken = tokens.Generate();
        var outbox = OutboxMessage.Create(
            PlatformInvitationDelivery.ConfirmationMessageType,
            JsonSerializer.Serialize(new PlatformInvitationDelivery.PlatformConfirmationEnvelope(identityId, invitationId)),
            now);
        context.OutboxMessages.Add(outbox);
        context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.Add(ConfirmationWindow)));
    }

    /// <summary>
    /// Whether a stored payload is the confirmation envelope for this identity and this invitation. It is read
    /// rather than matched as text because the column is <c>jsonb</c>: PostgreSQL is free to reorder and respace
    /// what it stores, so equality against a serialized string is a comparison that can silently stop matching.
    /// </summary>
    private static bool Names(string payload, Guid identityId, Guid invitationId)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<PlatformInvitationDelivery.PlatformConfirmationEnvelope>(payload);
            return envelope is not null && envelope.IdentityId == identityId && envelope.InvitationId == invitationId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } provider &&
        provider.GetType().GetProperty("SqlState")?.GetValue(provider) as string == "23505";
}
