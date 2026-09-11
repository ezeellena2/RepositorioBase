using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class AccountReactivationDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    ITokenHasher tokenHasher,
    TimeProvider timeProvider,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public string MessageType => RequestAccountReactivationCommandHandler.MessageType;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<RequestAccountReactivationCommandHandler.Envelope>(payload);
        OutboxPayload.RequireId(envelope.RequestId);
        if (string.IsNullOrEmpty(token)) return null;

        var delivery = await (from ticket in context.AccountReactivationRequests.AsNoTracking()
                              join user in context.Users.AsNoTracking() on ticket.IdentityId equals user.Id
                              where ticket.Id == envelope.RequestId
                              select new { Ticket = ticket, user.Email, user.Status }).SingleOrDefaultAsync(cancellationToken);
        if (delivery is null || !delivery.Ticket.IsPendingAt(timeProvider.GetUtcNow()) ||
            delivery.Status != IdentityAccountStatus.SelfDeactivated || string.IsNullOrWhiteSpace(delivery.Email) ||
            tokenHasher.Of(token) != delivery.Ticket.TokenHash) return null;

        // No security-version check: setting a password while parked must not invalidate the way back (E1).
        var link = RegistrationIntentRecipient.Link(options.Value, "/account/reactivate");
        return await localizer.CreateAsync(
            delivery.Email,
            null,
            deliveryLanguage,
            "AccountReactivationSubject",
            "AccountReactivationBody",
            cancellationToken,
            link,
            Uri.EscapeDataString(token));
    }
}

public abstract class AccountLifecycleNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : IOutboxDeliveryHandler
{
    public abstract string MessageType { get; }
    protected abstract string ResourcePrefix { get; }
    protected abstract string Path { get; }
    public bool RequiresSecret => false;

    public async Task<IdentityEmail?> PrepareAsync(
        string payload,
        string? token,
        string? deliveryLanguage,
        CancellationToken cancellationToken)
    {
        var envelope = OutboxPayload.Deserialize<IdentityLifecycleNotice.Envelope>(payload);
        OutboxPayload.RequireId(envelope.IdentityId);
        var recipient = await context.Users.AsNoTracking().Where(user => user.Id == envelope.IdentityId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var link = RegistrationIntentRecipient.Link(options.Value, Path);
        return await localizer.CreateAsync(
            recipient,
            null,
            deliveryLanguage,
            $"{ResourcePrefix}Subject",
            $"{ResourcePrefix}Body",
            cancellationToken,
            link);
    }
}

public sealed class AccountSelfDeactivatedNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : AccountLifecycleNoticeDeliveryHandler(context, options, localizer)
{
    public override string MessageType => IdentityLifecycleNotice.SelfDeactivatedMessageType;
    protected override string ResourcePrefix => "AccountSelfDeactivated";
    protected override string Path => "/account/reactivation-request";
}

public sealed class AccountAdministrativelySuspendedNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : AccountLifecycleNoticeDeliveryHandler(context, options, localizer)
{
    public override string MessageType => IdentityLifecycleNotice.AdministrativelySuspendedMessageType;
    protected override string ResourcePrefix => "AccountAdministrativelySuspended";
    protected override string Path => "/login";
}

public sealed class AccountReactivatedNoticeDeliveryHandler(
    ApplicationDbContext context,
    IOptions<IdentityEmailOptions> options,
    IdentityEmailLocalizer localizer)
    : AccountLifecycleNoticeDeliveryHandler(context, options, localizer)
{
    public override string MessageType => IdentityLifecycleNotice.ReactivatedMessageType;
    protected override string ResourcePrefix => "AccountReactivated";
    protected override string Path => "/login";
}
