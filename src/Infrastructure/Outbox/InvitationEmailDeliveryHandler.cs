using System.Text.Json;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class InvitationEmailDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.invitation.requested";

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<InvitationEnvelope>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (envelope is null || envelope.InvitationId == Guid.Empty) return null;
        var id = InvitationId.From(envelope.InvitationId);
        var recipient = await context.Invitations.AsNoTracking().Where(item => item.Id == id)
            .Select(item => item.NormalizedEmail).SingleOrDefaultAsync(cancellationToken);
        if (recipient is null) return null;
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/invitations/accept").AbsoluteUri;
        return new IdentityEmail(recipient, "You have been invited", $"Open this link to accept: {link}#token={Uri.EscapeDataString(token!)}");
    }

    private sealed record InvitationEnvelope(Guid InvitationId);
}
