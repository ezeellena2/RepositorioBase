using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Outbox;

public sealed class InvitedConfirmationDeliveryHandler(ApplicationDbContext context, IOptions<IdentityEmailOptions> options)
    : EmailConfirmationDeliveryHandler(context, options)
{
    public override string MessageType => "identity.invitation.confirmation.requested";
}
