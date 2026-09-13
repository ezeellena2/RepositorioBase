using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace CleanArchitecture.Infrastructure.Email;

/// <summary>
/// Sends through Gmail's SMTP submission server with an app password, so a developer can receive real mail in a
/// real inbox without a provider account or a verified domain.
/// <para>
/// It is a local and development option, not a production default. SMTP has no idempotency key: a retry after an
/// uncertain answer — a connection lost after the message was handed over — can deliver a second copy. The outbox
/// message id is used as the <c>Message-Id</c>, so the copies are recognisably the same message, but nothing on the
/// server side refuses the second one the way Resend's key does.
/// </para>
/// <para>
/// Nothing here logs, and nothing the server says is kept. A failure becomes a receipt with no provider text in
/// it, classified by what retrying could change: a 4xx reply or a dropped connection is transient; a 5xx reply, a
/// refused credential or a TLS mode the server does not speak is permanent.
/// </para>
/// </summary>
public sealed class GmailSmtpEmailSender(IOptions<IdentityEmailOptions> options, ISmtpMailTransport transport) : IIdentityEmailSender
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    private static readonly EmailDeliveryReceipt Transient = new(false, null, false);
    private static readonly EmailDeliveryReceipt Permanent = new(false, null, true);

    public void ValidateConfiguration()
    {
        if (!options.Value.IsValid() ||
            !options.Value.TryResolveProvider(out var provider) || provider != IdentityEmailProvider.GmailSmtp)
        {
            throw new InvalidOperationException("Identity email delivery is not configured.");
        }
    }

    /// <summary>
    /// Everything that decides where the message goes and who it is from. A changed server, account or credential
    /// can be a different mailbox, so it fails closed while a delivery is uncertain; the password is hashed first and
    /// only the combined fingerprint is persisted.
    /// </summary>
    public string GetRequestFingerprint(string recipient, string subject, string body)
    {
        var email = options.Value;
        var smtp = email.Smtp;
        var credentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(smtp.Password ?? string.Empty)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            provider = nameof(IdentityEmailProvider.GmailSmtp),
            host = smtp.Host,
            port = smtp.Port,
            startTls = smtp.UseStartTls,
            username = smtp.Username,
            credentialHash,
            from = email.FromAddress,
            to = new[] { recipient },
            subject,
            text = body
        }))));
    }

    public async Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (!Guid.TryParseExact(idempotencyKey, "D", out _))
        {
            throw new ArgumentException("The delivery key must be an outbox message ID.", nameof(idempotencyKey));
        }

        var email = options.Value;
        var smtp = email.Smtp;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Deadline);
        try
        {
            using var message = Compose(email.FromAddress!, recipient, subject, body, idempotencyKey);
            await transport.SendAsync(
                new SmtpSubmission(smtp.Host!, smtp.Port!.Value, smtp.UseStartTls, smtp.Username!, smtp.Password!, message),
                timeout.Token);
            return new EmailDeliveryReceipt(true, $"gmail-smtp-{idempotencyKey}", false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Transient; }
        catch (SmtpCommandException failure) { return (int)failure.StatusCode >= 500 ? Permanent : Transient; }
        catch (AuthenticationException) { return Permanent; } // SaslException included.
        catch (SslHandshakeException) { return Permanent; }
        catch (NotSupportedException) { return Permanent; }
        catch (ParseException) { return Permanent; }
        catch (SmtpProtocolException) { return Transient; }
        catch (ServiceNotConnectedException) { return Transient; }
        catch (SocketException) { return Transient; }
        catch (IOException) { return Transient; }
        catch (TimeoutException) { return Transient; }
    }

    private static MimeMessage Compose(string from, string recipient, string subject, string body, string idempotencyKey)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        // Stable across attempts, so a duplicate left by an uncertain retry is the same message to a mail client.
        message.MessageId = $"{idempotencyKey}@{from[(from.LastIndexOf('@') + 1)..]}";
        message.Body = new TextPart(TextFormat.Plain) { Text = body };
        return message;
    }
}
