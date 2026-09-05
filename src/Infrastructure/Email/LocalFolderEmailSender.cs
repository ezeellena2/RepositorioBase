using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Email;

/// <summary>
/// Writes each message to a folder instead of sending it, so a local run has real, clickable links without a
/// provider, an account, or any chance of reaching a real mailbox.
/// <para>
/// It exists because the alternative was worse. The tokens are sealed with Data Protection keys held by the
/// application process, so nobody outside it can read them out of the database — which left a local operator with
/// no way to follow their own invitation, and left the acceptance suite confirming addresses with SQL rather than
/// through the screen a person uses.
/// </para>
/// <para>
/// It fails closed on the environment rather than on a flag. A folder full of live invitation links is a mailbox
/// with no password on it, so this refuses to run anywhere except an explicit Development, Test or Testing host —
/// and it refuses at configuration time, which is before the dispatcher will claim a single message.
/// </para>
/// </summary>
public sealed class LocalFolderEmailSender(IOptions<IdentityEmailOptions> options, IHostEnvironment environment) : IIdentityEmailSender
{
    public void ValidateConfiguration()
    {
        if (!IdentityEmailOptions.IsLocalEnvironment(environment))
        {
            throw new InvalidOperationException(
                $"Local folder email delivery is not permitted in the {environment.EnvironmentName} environment.");
        }

        if (!options.Value.IsValid())
        {
            throw new InvalidOperationException("Identity email delivery is not configured.");
        }

        Directory.CreateDirectory(options.Value.LocalDropPath!);
    }

    /// <summary>
    /// The same shape the provider adapter fingerprints, minus a credential there is none of. A message whose
    /// rendered content changed between attempts still fails closed rather than delivering something else.
    /// </summary>
    public string GetRequestFingerprint(string recipient, string subject, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { local = true, from = options.Value.FromAddress, to = new[] { recipient }, subject, text = body }))));

    public async Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (!Guid.TryParseExact(idempotencyKey, "D", out _))
        {
            throw new ArgumentException("The delivery key must be an outbox message ID.", nameof(idempotencyKey));
        }

        var path = Path.Combine(options.Value.LocalDropPath!, $"{idempotencyKey}.txt");

        // Keyed by the outbox message, so a retry after an uncertain write reconciles onto the same file and the
        // same receipt — which is exactly what the provider's idempotency key buys, and what the dispatcher's
        // replay logic expects.
        if (!File.Exists(path))
        {
            var content = new StringBuilder()
                .Append("To: ").AppendLine(recipient)
                .Append("From: ").AppendLine(options.Value.FromAddress)
                .Append("Subject: ").AppendLine(subject)
                .Append("Date: ").AppendLine(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine()
                .AppendLine(body)
                .ToString();
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }

        return new EmailDeliveryReceipt(true, $"local-folder-{idempotencyKey}", false);
    }
}
