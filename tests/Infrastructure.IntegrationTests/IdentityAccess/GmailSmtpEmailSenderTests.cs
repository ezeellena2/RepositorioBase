using System.Net.Sockets;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Infrastructure.Outbox;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// Gmail SMTP delivery, driven through a recording transport so nothing here contacts Google. What is pinned is
/// the part the dispatcher depends on: which sender a configuration selects, that a broken one refuses to start,
/// that a failure is classified without carrying the server's words, and that the retry fingerprint notices a
/// change of mailbox.
/// </summary>
public sealed class GmailSmtpEmailSenderTests
{
    private const string Password = "private-app-password";
    private const string Body = "Open this link to confirm: https://app.example.test/confirm-email#token=private-token";

    [Test]
    public async Task Configured_sender_submits_the_rendered_message_through_the_configured_server()
    {
        var transport = new RecordingTransport();
        var sender = new GmailSmtpEmailSender(Options.Create(ValidOptions()), transport);
        var id = Guid.NewGuid().ToString("D");

        var receipt = await sender.SendAsync("recipient@example.test", "Confirm", Body, id, CancellationToken.None);

        receipt.ShouldBe(new EmailDeliveryReceipt(true, $"gmail-smtp-{id}", false));
        transport.Host.ShouldBe("smtp.gmail.com");
        transport.Port.ShouldBe(587);
        transport.UseStartTls.ShouldBeTrue();
        transport.Username.ShouldBe("sender@gmail.com");
        transport.Password.ShouldBe(Password);
        transport.From.ShouldBe("sender@gmail.com");
        transport.To.ShouldBe("recipient@example.test");
        transport.Subject.ShouldBe("Confirm");
        transport.Text.ShouldBe(Body);
        transport.MessageId.ShouldBe($"{id}@gmail.com", "a retry carries the same Message-Id, so a duplicate is recognisably the same message");
        receipt.ToString().ShouldNotContain(Password);
        receipt.ToString().ShouldNotContain("private-token");
    }

    private static IEnumerable<TestCaseData> Failures()
    {
        yield return new TestCaseData(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxBusy, Secrets()), false).SetName("SMTP 4xx is transient");
        yield return new TestCaseData(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, Secrets()), true).SetName("SMTP 5xx is permanent");
        yield return new TestCaseData(new AuthenticationException(Secrets()), true).SetName("Refused credential is permanent");
        yield return new TestCaseData(new SslHandshakeException(Secrets()), true).SetName("TLS mode mismatch is permanent");
        yield return new TestCaseData(new NotSupportedException(Secrets()), true).SetName("Missing STARTTLS or AUTH is permanent");
        yield return new TestCaseData(new SmtpProtocolException(Secrets()), false).SetName("Dropped SMTP session is transient");
        yield return new TestCaseData(new SocketException((int)SocketError.ConnectionRefused), false).SetName("Unreachable server is transient");
        yield return new TestCaseData(new IOException(Secrets()), false).SetName("Broken stream is transient");
        yield return new TestCaseData(new OperationCanceledException(Secrets()), false).SetName("Missed deadline is transient");
    }

    [TestCaseSource(nameof(Failures))]
    public async Task Smtp_failures_are_redacted_and_classified(Exception failure, bool permanent)
    {
        var sender = new GmailSmtpEmailSender(Options.Create(ValidOptions()), new RecordingTransport(failure));

        var receipt = await sender.SendAsync("recipient@example.test", "Notice", Body, Guid.NewGuid().ToString("D"), CancellationToken.None);

        receipt.Delivered.ShouldBeFalse();
        receipt.IsPermanentFailure.ShouldBe(permanent);
        receipt.ProviderReceipt.ShouldBeNull();
        receipt.ToString().ShouldNotContain("private");
    }

    [Test]
    public async Task Caller_cancellation_is_not_mistaken_for_a_failed_delivery()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var sender = new GmailSmtpEmailSender(Options.Create(ValidOptions()), new RecordingTransport(new OperationCanceledException(cancellation.Token)));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            sender.SendAsync("recipient@example.test", "Notice", Body, Guid.NewGuid().ToString("D"), cancellation.Token));
    }

    [Test]
    public void Configuration_refusal_never_carries_the_credential()
    {
        var options = ValidOptions();
        options.Smtp.Host = null;
        var sender = new GmailSmtpEmailSender(Options.Create(options), new RecordingTransport());

        var refusal = Should.Throw<InvalidOperationException>(sender.ValidateConfiguration);

        refusal.Message.ShouldBe("Identity email delivery is not configured.");
        refusal.ToString().ShouldNotContain(Password);
    }

    [TestCase("password")]
    [TestCase("from")]
    [TestCase("host")]
    [TestCase("port")]
    [TestCase("username")]
    public void A_change_of_mailbox_changes_the_retry_fingerprint(string setting)
    {
        var options = ValidOptions();
        var sender = new GmailSmtpEmailSender(Options.Create(options), new RecordingTransport());
        var first = sender.GetRequestFingerprint("recipient@example.test", "Confirm", Body);
        sender.GetRequestFingerprint("recipient@example.test", "Confirm", Body).ShouldBe(first, "an unchanged request must reconcile onto itself");

        switch (setting)
        {
            case "password": options.Smtp.Password = "rotated-app-password"; break;
            case "from": options.FromAddress = "other@gmail.com"; break;
            case "host": options.Smtp.Host = "smtp.example.test"; break;
            case "port": options.Smtp.Port = 465; break;
            case "username": options.Smtp.Username = "other@gmail.com"; break;
        }

        var changed = sender.GetRequestFingerprint("recipient@example.test", "Confirm", Body);
        changed.ShouldNotBe(first);
        changed.ShouldNotContain(Password);
    }

    [TestCase("GmailSmtp", null, typeof(GmailSmtpEmailSender))]
    [TestCase("gmailsmtp", null, typeof(GmailSmtpEmailSender))]
    [TestCase(null, "drop", typeof(LocalFolderEmailSender))]
    [TestCase(null, null, typeof(IdentityEmailAdapter))]
    [TestCase("Resend", null, typeof(IdentityEmailAdapter))]
    public void Worker_registration_selects_the_sender_the_configuration_names(string? provider, string? dropPath, Type expected)
    {
        using var databaseScope = TestServices.CreateScope();
        var builder = GmailWorkerBuilder(databaseScope);
        builder.Configuration["IdentityAccess:Email:Provider"] = provider;
        builder.Configuration["IdentityAccess:Email:LocalDropPath"] = dropPath is null ? null : Path.Combine(Path.GetTempPath(), $"local-mail-{Guid.NewGuid():N}");
        builder.AddOutboxWorkerServices();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IIdentityEmailSender>().ShouldBeOfType(expected);
    }

    [TestCase("Smtp:Host")]
    [TestCase("Smtp:Port")]
    [TestCase("Smtp:Username")]
    [TestCase("Smtp:Password")]
    [TestCase("FromAddress")]
    [TestCase("PublicOrigin")]
    public async Task Missing_gmail_setting_fails_at_startup(string setting)
    {
        using var databaseScope = TestServices.CreateScope();
        var builder = GmailWorkerBuilder(databaseScope);
        builder.Configuration[$"IdentityAccess:Email:{setting}"] = null;
        builder.AddOutboxWorkerServices();
        using var host = builder.Build();

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync());

        refusal.Message.ShouldBe("Identity email delivery is not configured.");
        refusal.ToString().ShouldNotContain(Password);
    }

    [Test]
    public async Task Gmail_selected_while_delivery_is_disabled_fails_at_startup()
    {
        using var databaseScope = TestServices.CreateScope();
        var builder = GmailWorkerBuilder(databaseScope);
        builder.Configuration["IdentityAccess:Email:Enabled"] = "false";
        builder.AddOutboxWorkerServices();
        using var host = builder.Build();

        (await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync()))
            .Message.ShouldBe("Identity email delivery is not configured.");
    }

    /// <summary>
    /// A developer who switched to Gmail but left the drop path behind would otherwise keep getting files and
    /// believe mail was being sent. The contradiction refuses to start instead of picking one.
    /// </summary>
    [Test]
    public async Task Gmail_selected_with_a_leftover_drop_path_fails_at_startup()
    {
        using var databaseScope = TestServices.CreateScope();
        var builder = GmailWorkerBuilder(databaseScope);
        builder.Configuration["IdentityAccess:Email:LocalDropPath"] = Path.Combine(Path.GetTempPath(), $"local-mail-{Guid.NewGuid():N}");
        builder.AddOutboxWorkerServices();
        using var host = builder.Build();

        (await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync()))
            .Message.ShouldBe("Identity email delivery is not configured.");
    }

    private static HostApplicationBuilder GmailWorkerBuilder(IServiceScope databaseScope)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration["ConnectionStrings:CleanArchitectureDb"] = databaseScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
        builder.Configuration["IdentityAccess:Email:Enabled"] = "true";
        builder.Configuration["IdentityAccess:Email:Provider"] = "GmailSmtp";
        builder.Configuration["IdentityAccess:Email:FromAddress"] = "sender@gmail.com";
        builder.Configuration["IdentityAccess:Email:PublicOrigin"] = "https://app.example.test";
        builder.Configuration["IdentityAccess:Email:Smtp:Host"] = "smtp.gmail.com";
        builder.Configuration["IdentityAccess:Email:Smtp:Port"] = "587";
        builder.Configuration["IdentityAccess:Email:Smtp:Username"] = "sender@gmail.com";
        builder.Configuration["IdentityAccess:Email:Smtp:Password"] = Password;
        builder.Configuration["IdentityAccess:Email:Smtp:UseStartTls"] = "true";
        return builder;
    }

    private static IdentityEmailOptions ValidOptions() => new()
    {
        Enabled = true,
        Provider = "GmailSmtp",
        FromAddress = "sender@gmail.com",
        PublicOrigin = "https://app.example.test",
        Smtp = new IdentitySmtpOptions
        {
            Host = "smtp.gmail.com",
            Port = 587,
            Username = "sender@gmail.com",
            Password = Password,
            UseStartTls = true
        }
    };

    // What a server or a socket might put in an exception: the credential, the recipient and the link.
    private static string Secrets() => $"535 {Password} recipient@example.test {Body}";

    private sealed class RecordingTransport(Exception? failure = null) : ISmtpMailTransport
    {
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public bool UseStartTls { get; private set; }
        public string? Username { get; private set; }
        public string? Password { get; private set; }
        public string? From { get; private set; }
        public string? To { get; private set; }
        public string? Subject { get; private set; }
        public string? Text { get; private set; }
        public string? MessageId { get; private set; }

        public Task SendAsync(SmtpSubmission submission, CancellationToken cancellationToken)
        {
            // Captured now: the sender disposes the message once the send returns.
            Host = submission.Host;
            Port = submission.Port;
            UseStartTls = submission.UseStartTls;
            Username = submission.Username;
            Password = submission.Password;
            From = submission.Message.From.Mailboxes.Single().Address;
            To = submission.Message.To.Mailboxes.Single().Address;
            Subject = submission.Message.Subject;
            Text = submission.Message.TextBody;
            MessageId = submission.Message.MessageId;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
