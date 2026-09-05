using System.Text.Json;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>Resolves an identity-only payload. Organization state is not required to deliver confirmation.</summary>
public class EmailConfirmationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public virtual string MessageType => "identity.confirmation.requested";

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (envelope is null || envelope.IdentityId == Guid.Empty) return null;
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/confirm-email").AbsoluteUri;
        return new IdentityEmail(recipient, "Confirm your email", $"Open this link to confirm: {link}#token={Uri.EscapeDataString(token!)}");
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId);
}
