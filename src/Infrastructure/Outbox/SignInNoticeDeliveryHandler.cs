using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class SignInNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.invitation.signin.notice.requested";
    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<NoticeEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IdentityId);
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        string? snapshotLanguage = null;
        if (envelope.InvitationId is { } invitationId && invitationId != Guid.Empty)
        {
            var id = OutboxPayload.RequireId(invitationId, InvitationId.From);
            snapshotLanguage = await context.Invitations.AsNoTracking()
                .Where(invitation => invitation.Id == id)
                .Select(invitation => invitation.Language)
                .SingleOrDefaultAsync(cancellationToken);
        }
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/login").AbsoluteUri;
        return await localizer.CreateAsync(
            recipient,
            snapshotLanguage,
            deliveryLanguage,
            "SignInNoticeSubject",
            "SignInNoticeBody",
            cancellationToken,
            link);
    }

    private sealed record NoticeEnvelope(Guid IdentityId, Guid? InvitationId = null);
}
