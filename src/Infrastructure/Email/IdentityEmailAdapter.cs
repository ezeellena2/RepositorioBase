using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Email;

public sealed class IdentityEmailOptions
{
    public const string SectionName = "IdentityAccess:Email";

    public string? FromAddress { get; set; }

    public string? PublicOrigin { get; set; }
}

/// <summary>
/// The configured provider. It fails closed: without a from-address and a public origin there is no way to send
/// a usable link, and silently dropping every confirmation and invitation would look exactly like a system where
/// nobody ever registers.
/// </summary>
public sealed class IdentityEmailAdapter(IOptions<IdentityEmailOptions> options) : IIdentityEmailSender
{
    public Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.FromAddress) || string.IsNullOrWhiteSpace(configuration.PublicOrigin))
        {
            throw new InvalidOperationException("Identity email delivery is not configured.");
        }

        // No provider is wired yet: Task 11 owns the loop, and the transport it hands off to is configuration.
        // Throwing here rather than answering "delivered" keeps an unconfigured deployment visibly broken.
        throw new NotSupportedException("No identity email transport is configured for this deployment.");
    }
}
