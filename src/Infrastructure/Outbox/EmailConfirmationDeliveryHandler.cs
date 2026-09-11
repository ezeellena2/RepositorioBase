using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>Resolves an identity-only payload. Organization state is not required to deliver confirmation.</summary>
public class EmailConfirmationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer) : IOutboxDeliveryHandler
{
    protected ApplicationDbContext Context => context;

    public virtual string MessageType => "identity.confirmation.requested";

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<ConfirmationEnvelope>(payload);
        OutboxPayload.RequireId(envelope.IdentityId);
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var snapshotLanguage = await ResolveSnapshotLanguageAsync(envelope.InvitationId, cancellationToken);
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/confirm-email").AbsoluteUri;
        return await localizer.CreateAsync(
            recipient,
            snapshotLanguage,
            deliveryLanguage,
            "ConfirmationSubject",
            "ConfirmationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token!));
    }

    protected virtual Task<string?> ResolveSnapshotLanguageAsync(Guid? invitationId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid? InvitationId = null);
}
