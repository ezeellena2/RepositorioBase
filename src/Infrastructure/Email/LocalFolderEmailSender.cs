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
        //
        // What counts as the prior attempt having succeeded is the file being COMPLETE, not the name existing. A
        // write interrupted midway leaves a truncated file; treating that as delivered would report success for a
        // message nobody can read, and the dispatcher would then delete the token ciphertext — losing the only
        // copy of the link. So a file that does not parse is republished rather than believed.
        if (!await IsCompleteAsync(path, recipient, subject, cancellationToken))
        {
            var content = new StringBuilder()
                .Append("To: ").AppendLine(recipient)
                .Append("From: ").AppendLine(options.Value.FromAddress)
                .Append("Subject: ").AppendLine(subject)
                .Append("Date: ").AppendLine(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine()
                .AppendLine(body)
                .ToString();
            await PublishAsync(path, content, cancellationToken);
        }

        return new EmailDeliveryReceipt(true, $"local-folder-{idempotencyKey}", false);
    }

    /// <summary>
    /// Writes beside the final name and moves it into place, so the message a reader can see is either absent or
    /// whole. A reader polling the folder — the acceptance journeys do exactly that — would otherwise be able to
    /// open a half-written file and act on a truncated link.
    /// </summary>
    private static async Task PublishAsync(string path, string content, CancellationToken cancellationToken)
    {
        // Beside it rather than in a temp directory: a move within one directory is the only kind the filesystem
        // performs atomically, and a cross-volume move degrades to a copy, which is the very thing being avoided.
        var pending = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            await File.WriteAllTextAsync(pending, content, cancellationToken);
            File.Move(pending, path, overwrite: true);
        }
        catch
        {
            // A failed publish leaves nothing behind to be mistaken for a delivery.
            try { File.Delete(pending); } catch (IOException) { /* The next attempt writes its own name. */ }
            throw;
        }
    }

    /// <summary>
    /// Whether a previous attempt actually published this message. It is the evidence that is checked, not the
    /// name: every header the writer emits must be present with the values this attempt would write, and there
    /// must be a body after the blank line. An empty or truncated file answers false and is republished.
    /// </summary>
    private async Task<bool> IsCompleteAsync(string path, string recipient, string subject, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;

        string content;
        try { content = await File.ReadAllTextAsync(path, cancellationToken); }
        catch (IOException) { return false; }

        // The blank line the writer emits, in whichever line ending the platform that wrote it uses.
        var bodyStart = -1;
        foreach (var blankLine in new[] { "\r\n\r\n", "\n\n" })
        {
            var index = content.IndexOf(blankLine, StringComparison.Ordinal);
            if (index >= 0) { bodyStart = index + blankLine.Length; break; }
        }

        if (bodyStart < 0 || string.IsNullOrWhiteSpace(content[bodyStart..])) return false;

        var headers = content[..bodyStart];
        return headers.Contains($"To: {recipient}", StringComparison.Ordinal) &&
               headers.Contains($"From: {options.Value.FromAddress}", StringComparison.Ordinal) &&
               headers.Contains($"Subject: {subject}", StringComparison.Ordinal) &&
               headers.Contains("Date: ", StringComparison.Ordinal);
    }
}
