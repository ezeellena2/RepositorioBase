using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class InvitationEmailDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.invitation.requested";

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<InvitationEnvelope>(payload);
        var id = OutboxPayload.RequireId(envelope.InvitationId, InvitationId.From);
        var invitation = await context.Invitations.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.NormalizedEmail, item.Language }).SingleOrDefaultAsync(cancellationToken);
        if (invitation is null) return null;
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/invitations/accept").AbsoluteUri;
        return await localizer.CreateAsync(
            invitation.NormalizedEmail,
            invitation.Language,
            deliveryLanguage,
            "InvitationSubject",
            "InvitationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }

    private sealed record InvitationEnvelope(Guid InvitationId);
}
