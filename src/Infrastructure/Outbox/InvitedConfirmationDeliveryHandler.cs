using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class InvitedConfirmationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : EmailConfirmationDeliveryHandler(context, options, localizer)
{
    public override string MessageType => "identity.invitation.confirmation.requested";

    protected override async Task<string?> ResolveSnapshotLanguageAsync(
        Guid? invitationId,
        CancellationToken cancellationToken)
    {
        if (invitationId is null || invitationId == Guid.Empty) return null;
        var id = OutboxPayload.RequireId(invitationId.Value, InvitationId.From);
        return await Context.Invitations.AsNoTracking()
            .Where(invitation => invitation.Id == id)
            .Select(invitation => invitation.Language)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
