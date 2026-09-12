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

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

/// <summary>
/// The public half of IA-REQ-016. It gets an invitee as far as being confirmable and no further: it never accepts
/// the invitation and never creates a membership.
/// <para>
/// Every outcome after the password gate is the same neutral success, because the caller must not be able to learn
/// whether the token is live, or — given a live one — whether the address already has an account. The password
/// gate comes first precisely so that it reads no state: it answers identically in all four combinations, and so
/// cannot serve as an oracle for either question (SPEC section 6).
/// </para>
/// </summary>
public sealed class RegisterInvitedUserCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    LocalizationSettings localization,
    TimeProvider timeProvider) : IRequestHandler<RegisterInvitedUserCommand, Result>
{
    /// <summary>
    /// Its own purpose, distinct from the organization registration's. Sharing one message type would force the
    /// confirmation handler to guess which envelope it was holding, and a guess that lands on the wrong one
    /// silently skips activating a tenant.
    /// </summary>
    internal const string InvitedConfirmationMessageType = "identity.invitation.confirmation.requested";

    /// <summary>
    /// The generic notice an address that already has an account receives (IA-REQ-016). It is a distinct purpose
    /// carrying no token or invitation details. Alongside the recipient identity identifier, its envelope retains
    /// only the InvitationId from the invitation so delivery can resolve the immutable language snapshot; whoever
    /// submitted the token is not proven to own that account, so the notice tells the real owner how to sign in and
    /// nothing else. Writing it is also what makes the existing branch cost the same work as the missing one.
    /// </summary>
    internal const string ExistingIdentityNoticeMessageType = "identity.invitation.signin.notice.requested";
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterInvitedUserCommand request, CancellationToken cancellationToken)
    {
        var passwordValidation = await identities.ValidatePasswordAsync(request.Password, cancellationToken);
        if (!passwordValidation.IsValid)
        {
            return Result.Failure(IdentityAccessErrors.PasswordPolicyFailed(
                new Dictionary<string, ValidationErrorDetail[]> { ["password"] = [.. passwordValidation.Errors] }));
        }

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var now = InvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
                var invitation = await InvitationDelivery.FindByTokenAsync(context, tokenHasher, request.Token, ct);

                // A token that resolves to nothing, one whose window has closed and one that was withdrawn are all
                // equally unusable, and they answer exactly as a live one does.
                if (invitation is null || !invitation.IsPendingAt(now))
                {
                    return Result.Success();
                }

                if (await identities.FindByEmailAsync(invitation.NormalizedEmail, ct) is { } existing)
                {
                    // The address already has an account. Credential input is ignored rather than applied — holding
                    // this token is not proof of owning that account — and the owner is told, generically, that
                    // someone tried to register with it and that they can simply sign in.
                    context.OutboxMessages.Add(OutboxMessage.Create(
                        ExistingIdentityNoticeMessageType,
                        JsonSerializer.Serialize(new IdentityConfirmationEnvelope(existing.Id, invitation.Id.Value)),
                        now));
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
                    // The address is one the aggregate accepts and ASP.NET Identity does not — a Unicode local part it
                    // refuses, or a competing request that created it between the lookup above and this call. Failing
                    // here would answer 400 for a real token while an unknown one answers 202, which is exactly the
                    // token oracle the neutral flow exists to prevent. The attempt simply produced no identity.
                    return Result.Success();
                }

                var rawToken = tokens.Generate();
                var outbox = OutboxMessage.Create(
                    InvitedConfirmationMessageType,
                    JsonSerializer.Serialize(new IdentityConfirmationEnvelope(creation.Account.Id, invitation.Id.Value)),
                    now);
                context.OutboxMessages.Add(outbox);
                context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.Add(ConfirmationWindow)));
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A competing request created the identity between the lookup and the create. The address
            // exists after all, which is the existing-identity branch, and that branch is neutral. Letting
            // the violation escape would answer 500 for a real token while an unknown one answers 202.
            return Result.Success();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } provider &&
        provider.GetType().GetProperty("SqlState")?.GetValue(provider) as string == "23505";

    /// <summary>
    /// The confirmation purpose for an identity that holds no membership yet. It deliberately carries no tenant
    /// and no membership: an invited registration must not create either, so a shape that could name them would
    /// invite a later change to start doing so (IA-REQ-016).
    /// </summary>
    internal sealed record IdentityConfirmationEnvelope(Guid IdentityId, Guid? InvitationId = null);
}
