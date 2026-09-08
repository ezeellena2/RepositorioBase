using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class AccountReactivationDeliveryHandler(
    ApplicationDbContext context, IOptions<IdentityEmailOptions> options, ITokenHasher tokenHasher, TimeProvider timeProvider)
    : IOutboxDeliveryHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public string MessageType => RequestAccountReactivationCommandHandler.MessageType;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        RequestAccountReactivationCommandHandler.Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RequestAccountReactivationCommandHandler.Envelope>(payload, Json); }
        catch (JsonException) { return null; }
        if (envelope is null || envelope.RequestId == Guid.Empty || string.IsNullOrEmpty(token)) return null;

        var delivery = await (from ticket in context.AccountReactivationRequests.AsNoTracking()
                              join user in context.Users.AsNoTracking() on ticket.IdentityId equals user.Id
                              where ticket.Id == envelope.RequestId
                              select new { Ticket = ticket, user.Email, user.Status }).SingleOrDefaultAsync(cancellationToken);
        if (delivery is null || !delivery.Ticket.IsPendingAt(timeProvider.GetUtcNow()) ||
            delivery.Status != IdentityAccountStatus.SelfDeactivated || string.IsNullOrWhiteSpace(delivery.Email) ||
            tokenHasher.Of(token) != delivery.Ticket.TokenHash) return null;

        // No security-version check: setting a password while parked must not invalidate the way back (E1).
        var link = RegistrationIntentRecipient.Link(options.Value, "/account/reactivate");
        return new IdentityEmail(delivery.Email, "Reactivate your account",
            $"Open this link and enter your current password to reactivate your account: {link}#token={Uri.EscapeDataString(token)}");
    }
}

public sealed class AccountLifecycleNoticeDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : IOutboxDeliveryHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public string MessageType => DeactivateAccountCommandHandler.NoticeMessageType;
    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(string payload, string? token, CancellationToken cancellationToken)
    {
        DeactivateAccountCommandHandler.NoticeEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<DeactivateAccountCommandHandler.NoticeEnvelope>(payload, Json); }
        catch (JsonException) { return null; }
        if (envelope is null || envelope.IdentityId == Guid.Empty) return null;
        var notice = envelope.Outcome switch
        {
            "self_deactivated" => (Subject: "Your account was deactivated", Body: "Your account was deactivated and its sessions were ended. To request reactivation, visit", Path: "/account/reactivation-request"),
            "administratively_suspended" => (Subject: "Your account status changed", Body: "An administrator suspended your account. This message does not restore access. To check your access, visit", Path: "/login"),
            "reactivated" => (Subject: "Your account status changed", Body: "An administrator lifted your account suspension. To check your current access, visit", Path: "/login"),
            _ => default
        };
        if (notice.Path is null) return null;
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, notice.Path);
        return new IdentityEmail(recipient, notice.Subject, $"{notice.Body} {link}");
    }
}
