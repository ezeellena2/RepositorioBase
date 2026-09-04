using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Outbox;

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
    TimeProvider timeProvider) : IRequestHandler<RegisterInvitedUserCommand, Result>
{
    /// <summary>
    /// Its own purpose, distinct from the organization registration's. Sharing one message type would force the
    /// confirmation handler to guess which envelope it was holding, and a guess that lands on the wrong one
    /// silently skips activating a tenant.
    /// </summary>
    internal const string InvitedConfirmationMessageType = "identity.invitation.confirmation.requested";
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterInvitedUserCommand request, CancellationToken cancellationToken)
    {
        if (request.Password is null || request.Password.Length > 256 ||
            !(await identities.ValidatePasswordAsync(request.Password, cancellationToken)).IsValid)
        {
            return Result.Failure(IdentityAccessErrors.InvalidInvitation());
        }

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

            if (await identities.FindByEmailAsync(invitation.NormalizedEmail, ct) is not null)
            {
                // The address already has an account. Credential input is ignored rather than applied: holding
                // this token is not proof of owning that account.
                return Result.Success();
            }

            var creation = await identities.CreatePendingAsync(invitation.NormalizedEmail, request.Password, ct);
            if (creation.Account is null)
            {
                return Result.Failure(IdentityAccessErrors.InvalidInvitation());
            }

            var rawToken = tokens.Generate();
            var outbox = OutboxMessage.Create(
                InvitedConfirmationMessageType,
                JsonSerializer.Serialize(new IdentityConfirmationEnvelope(creation.Account.Id)),
                now);
            context.OutboxMessages.Add(outbox);
            context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.Add(ConfirmationWindow)));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// The confirmation purpose for an identity that holds no membership yet. It deliberately carries no tenant
    /// and no membership: an invited registration must not create either, so a shape that could name them would
    /// invite a later change to start doing so (IA-REQ-016).
    /// </summary>
    internal sealed record IdentityConfirmationEnvelope(Guid IdentityId);
}
