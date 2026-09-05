using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
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
    TimeProvider timeProvider) : IRequestHandler<RegisterPlatformInviteeCommand, Result>
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    public async Task<Result> Handle(RegisterPlatformInviteeCommand request, CancellationToken cancellationToken)
    {
        if (request.Password is null || request.Password.Length > 256 ||
            !(await identities.ValidatePasswordAsync(request.Password, cancellationToken)).IsValid)
        {
            return Result.Failure(IdentityAccessErrors.InvalidInvitation());
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
                    context.OutboxMessages.Add(OutboxMessage.Create(
                        PlatformInvitationDelivery.ExistingIdentityNoticeMessageType,
                        JsonSerializer.Serialize(new PlatformInvitationDelivery.PlatformConfirmationEnvelope(existing.Id, invitation.Id.Value)),
                        now));
                    await context.SaveChangesAsync(ct);
                    return Result.Success();
                }

                var creation = await identities.CreatePendingAsync(invitation.NormalizedEmail, request.Password, ct);
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } provider &&
        provider.GetType().GetProperty("SqlState")?.GetValue(provider) as string == "23505";
}
