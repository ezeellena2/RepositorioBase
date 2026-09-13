using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Email;

public enum IdentityEmailProvider
{
    LocalFolder,
    Resend,
    GmailSmtp
}

/// <summary>
/// The SMTP submission server for <see cref="IdentityEmailProvider.GmailSmtp"/>. There is no plaintext mode: the
/// connection is either upgraded with STARTTLS or encrypted from the first byte, because the password crosses it.
/// </summary>
public sealed class IdentitySmtpOptions
{
    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Username { get; set; }

    /// <summary>A Google app password, supplied from user secrets or the environment and never persisted.</summary>
    public string? Password { get; set; }

    /// <summary><c>true</c> upgrades with STARTTLS (port 587); <c>false</c> uses implicit TLS (port 465).</summary>
    public bool UseStartTls { get; set; } = true;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Host) && !Host.Any(char.IsWhiteSpace) &&
        Port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}

public sealed class IdentityEmailOptions
{
    public const string SectionName = "IdentityAccess:Email";

    public bool Enabled { get; set; }

    /// <summary>
    /// <c>LocalFolder</c>, <c>Resend</c> or <c>GmailSmtp</c>. Unset keeps the original selection: a local drop path
    /// writes to a folder, and anything else goes to Resend.
    /// </summary>
    public string? Provider { get; set; }

    public IdentitySmtpOptions Smtp { get; set; } = new();

    public string? ApiKey { get; set; }

    public string? FromAddress { get; set; }

    public string? PublicOrigin { get; set; }

    /// <summary>
    /// A folder to write messages into instead of sending them. Setting it selects local delivery, which is
    /// permitted only in an explicit Development, Test or Testing host — a folder of live invitation links is a
    /// mailbox with no password on it.
    /// </summary>
    public string? LocalDropPath { get; set; }

    public bool DeliversLocally => !string.IsNullOrWhiteSpace(LocalDropPath);

    /// <summary>The environments a local drop may exist in, which is the same list Data Protection uses.</summary>
    public static bool IsLocalEnvironment(Microsoft.Extensions.Hosting.IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test") || environment.IsEnvironment("Testing");

    /// <summary>
    /// Which sender this configuration asks for. A named provider that contradicts the drop path — a real provider
    /// with a folder still configured, or a folder provider without one — resolves to nothing rather than to a
    /// guess, so a developer who switched to real delivery cannot keep writing files without noticing.
    /// </summary>
    public bool TryResolveProvider(out IdentityEmailProvider provider)
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            provider = DeliversLocally ? IdentityEmailProvider.LocalFolder : IdentityEmailProvider.Resend;
            return true;
        }

        // Names only: Enum.TryParse would also accept "2" or "LocalFolder, GmailSmtp".
        var name = Enum.GetNames<IdentityEmailProvider>()
            .FirstOrDefault(candidate => string.Equals(candidate, Provider.Trim(), StringComparison.OrdinalIgnoreCase));
        provider = name is null ? default : Enum.Parse<IdentityEmailProvider>(name);
        return name is not null && (provider == IdentityEmailProvider.LocalFolder) == DeliversLocally;
    }

    // A local drop needs no credential, because there is no provider to authenticate to. Everything else is
    // still required: without a from-address and a public origin there is no usable link to write down.
    public bool IsValid() =>
        TryResolveProvider(out var provider) &&
        provider switch
        {
            IdentityEmailProvider.LocalFolder => true,
            // Gmail is chosen deliberately, for real mail, so a disabled one is a mistake rather than a default.
            IdentityEmailProvider.GmailSmtp => Enabled && Smtp.IsValid(),
            _ => !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.Any(char.IsWhiteSpace)
        } &&
        System.Net.Mail.MailAddress.TryCreate(FromAddress, out var from) && from.Address == FromAddress &&
        Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) && origin.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment) && origin.AbsolutePath == "/";
}

/// <summary>
/// The configured provider. It fails closed: without a from-address and a public origin there is no way to send
/// a usable link, and silently dropping every confirmation and invitation would look exactly like a system where
/// nobody ever registers.
/// </summary>
public sealed class IdentityEmailAdapter(IOptions<IdentityEmailOptions> options, HttpClient client) : IIdentityEmailSender
{
    public void ValidateConfiguration()
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Identity email delivery is not configured.");
    }

    public string GetRequestFingerprint(string recipient, string subject, string body)
    {
        // A new credential can belong to a different provider account. Fail closed on any rotation while a
        // delivery is uncertain; only the combined fingerprint is persisted, never the key or its raw bytes.
        var credentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey ?? string.Empty)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { credentialHash, from = options.Value.FromAddress, to = new[] { recipient }, subject, text = body }))));
    }

    public async Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (!Guid.TryParseExact(idempotencyKey, "D", out _)) throw new ArgumentException("The delivery key must be an outbox message ID.", nameof(idempotencyKey));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new { from = options.Value.FromAddress, to = new[] { recipient }, subject, text = body });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var requestToken = timeout.Token;
        var permanentResponse = false;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return new(false, null, false);
            permanentResponse = !response.IsSuccessStatusCode;
            if (permanentResponse && response.StatusCode != HttpStatusCode.Conflict)
                return new(false, null, true);
            await response.Content.LoadIntoBufferAsync(16 * 1024, requestToken);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestToken));
            if (response.IsSuccessStatusCode)
            {
                var id = payload.RootElement.TryGetProperty("id", out var value) ? value.GetString() : null;
                return Guid.TryParseExact(id, "D", out _) ? new(true, id, false) : new(false, null, false);
            }
            var concurrent = response.StatusCode == HttpStatusCode.Conflict &&
                payload.RootElement.TryGetProperty("name", out var name) && name.GetString() == "concurrent_idempotent_requests";
            return new(false, null, !concurrent);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null, false); }
        catch (HttpRequestException) { return new(false, null, permanentResponse); }
        catch (JsonException) { return new(false, null, permanentResponse); }
        catch (InvalidOperationException) { return new(false, null, permanentResponse); }
    }
}
