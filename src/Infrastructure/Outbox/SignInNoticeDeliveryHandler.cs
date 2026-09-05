using System.Text.Json;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class SignInNoticeDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options) : IOutboxDeliveryHandler
{
    public string MessageType => "identity.invitation.signin.notice.requested";
    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<NoticeEnvelope>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (envelope is null || envelope.IdentityId == Guid.Empty) return null;
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var link = new Uri(new Uri(options.Value.PublicOrigin!, UriKind.Absolute), "/login").AbsoluteUri;
        return new IdentityEmail(recipient, "Sign in to your account", $"A registration was requested for your email. You can sign in at {link}.");
    }

    private sealed record NoticeEnvelope(Guid IdentityId);
}
