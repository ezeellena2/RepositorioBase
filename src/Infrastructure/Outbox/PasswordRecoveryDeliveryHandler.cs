using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// The reset link. The recipient is read from the identity the request belongs to rather than from the payload, so
/// the address never enters a stored envelope, and the token arrives in the fragment where no server logs it.
/// </summary>
public sealed class PasswordRecoveryDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string MessageType => RequestPasswordRecoveryCommandHandler.MessageType;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        RequestPasswordRecoveryCommandHandler.Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RequestPasswordRecoveryCommandHandler.Envelope>(payload, Json); }
        catch (JsonException) { return null; }
        if (envelope is null || envelope.RequestId == Guid.Empty) return null;

        var recipient = await (from request in context.PasswordResetRequests.AsNoTracking()
                               join user in context.Users.AsNoTracking() on request.IdentityId equals user.Id
                               where request.Id == envelope.RequestId
                               select user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, "/credentials/reset");
        return new IdentityEmail(
            recipient,
            "Reset your password",
            $"Open this link to choose a new password: {link}#token={Uri.EscapeDataString(token!)}");
    }
}
