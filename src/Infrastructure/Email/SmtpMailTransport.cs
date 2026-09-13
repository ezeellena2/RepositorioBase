using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CleanArchitecture.Infrastructure.Email;

/// <summary>
/// One message for one SMTP session. A class rather than a record on purpose: a record's generated
/// <c>ToString</c> would print the password into whatever formats it.
/// </summary>
public sealed class SmtpSubmission(string host, int port, bool useStartTls, string username, string password, MimeMessage message)
{
    public string Host { get; } = host;

    public int Port { get; } = port;

    public bool UseStartTls { get; } = useStartTls;

    public string Username { get; } = username;

    public string Password { get; } = password;

    public MimeMessage Message { get; } = message;
}

/// <summary>
/// The network half of SMTP delivery, kept apart so the sender's classification and fingerprinting can be tested
/// without a server. Failures surface as MailKit and socket exceptions; deciding what they mean is the sender's job.
/// </summary>
public interface ISmtpMailTransport
{
    Task SendAsync(SmtpSubmission submission, CancellationToken cancellationToken);
}

public sealed class MailKitSmtpTransport : ISmtpMailTransport
{
    public async Task SendAsync(SmtpSubmission submission, CancellationToken cancellationToken)
    {
        // No protocol logger, deliberately: MailKit's logger writes the AUTH exchange and the message itself.
        using var client = new SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(
            submission.Host,
            submission.Port,
            submission.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
            cancellationToken);

        // The credential is a password, so an OAuth mechanism the server advertises is never the one to try.
        client.AuthenticationMechanisms.Remove("XOAUTH2");
        await client.AuthenticateAsync(submission.Username, submission.Password, cancellationToken);
        await client.SendAsync(submission.Message, cancellationToken);

        try
        {
            using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.DisconnectAsync(true, quit.Token);
        }
        catch (Exception)
        {
            // The server has already accepted the message. Reporting a failed QUIT as a failed delivery would make
            // the dispatcher retry, and SMTP has no idempotency key to stop that becoming a second email.
        }
    }
}
