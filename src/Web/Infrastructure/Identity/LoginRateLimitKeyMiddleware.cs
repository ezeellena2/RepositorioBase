using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Derives the opaque login partition keys before <c>UseRateLimiter</c> runs. It acts only on the endpoint that
/// carries the <see cref="LoginRateLimitPartitioner.PolicyName"/> policy, decodes the JSON body the way the endpoint
/// binder does (declared charset honored, byte order marks tolerated), buffers it so the endpoint can still bind it,
/// and never logs the address, the email or the keys. A body that cannot be attributed to an account but might still
/// reach the endpoint falls into a shared fail-closed partition instead of escaping the account limit.
/// </summary>
public sealed class LoginRateLimitKeyMiddleware(RequestDelegate next)
{
    public const string AccountKeyItem = "identity.login.account-key";
    public const string ClientKeyItem = "identity.login.client-key";

    /// <summary>Largest body the login route accepts and inspects; a legitimate login payload is a few hundred bytes.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>Shared account partition for bodies larger than <see cref="MaxBodyBytes"/>.</summary>
    public const string OversizedBodyPartition = "oversized-body";

    /// <summary>Shared account partition for bodies whose declared charset cannot be decoded.</summary>
    public const string UndecodableBodyPartition = "undecodable-body";

    private const string EmailProperty = "email";

    /// <summary>Hosts that cannot report a remote address share one partition instead of escaping the limit.</summary>
    private const string UnknownClient = "unknown";

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsLoginEndpoint(context))
        {
            context.Items[ClientKeyItem] = LoginRateLimitPartitioner.ClientKey(ClientAddress(context.Connection.RemoteIpAddress));
            LimitBodySize(context);
            var account = await ReadAccountAsync(context.Request, context.RequestAborted);
            if (account.Email is not null)
            {
                context.Items[AccountKeyItem] = LoginRateLimitPartitioner.AccountKey(account.Email);
            }
            else if (account.Sentinel is not null)
            {
                context.Items[AccountKeyItem] = LoginRateLimitPartitioner.SentinelKey(account.Sentinel);
            }
        }

        await next(context);
    }

    public static bool IsLoginEndpoint(HttpContext context) =>
        string.Equals(
            context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
            LoginRateLimitPartitioner.PolicyName,
            StringComparison.Ordinal);

    /// <summary>IPv4 clients behind dual-stack listeners arrive as IPv4-mapped IPv6 addresses; both spellings are one client.</summary>
    private static string ClientAddress(IPAddress? address) =>
        address is null ? UnknownClient : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

    private static void LimitBodySize(HttpContext context)
    {
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = MaxBodyBytes;
        }
    }

    private static async Task<AccountReading> ReadAccountAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            // Not JSON: the endpoint rejects the media type and no credential attempt happens.
            return AccountReading.None;
        }

        if (request.ContentLength is > MaxBodyBytes)
        {
            return AccountReading.Shared(OversizedBodyPartition);
        }

        if (!TryGetEncoding(request.ContentType, out var encoding))
        {
            return AccountReading.Shared(UndecodableBodyPartition);
        }

        request.EnableBuffering(MaxBodyBytes);
        try
        {
            var bytes = await ReadUpToAsync(request.Body, MaxBodyBytes + 1, cancellationToken);
            if (bytes is null)
            {
                return AccountReading.Shared(OversizedBodyPartition);
            }

            using var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(await reader.ReadToEndAsync(cancellationToken));
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? AccountReading.For(LoginRateLimitPartitioner.NormalizeEmail(FindEmail(document.RootElement)))
                : AccountReading.None;
        }
        catch (JsonException)
        {
            // Malformed JSON is the endpoint's 400 to report; no credential attempt happens.
            return AccountReading.None;
        }
        catch (DecoderFallbackException)
        {
            return AccountReading.Shared(UndecodableBodyPartition);
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    /// <summary>Mirrors the framework's JSON binding: no charset means UTF-8, anything else must be a known encoding.</summary>
    private static bool TryGetEncoding(string? contentType, out Encoding encoding)
    {
        encoding = Encoding.UTF8;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) || StringSegment.IsNullOrEmpty(mediaType.Charset))
        {
            return true;
        }

        try
        {
            encoding = Encoding.GetEncoding(mediaType.Charset.Value!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads at most <paramref name="limit"/> bytes; null means the body is longer than the limit.</summary>
    private static async Task<byte[]?> ReadUpToAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[limit];
        var total = 0;
        while (total < limit)
        {
            var read = await body.ReadAsync(buffer.AsMemory(total, limit - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total >= limit ? null : buffer[..total];
    }

    private static string? FindEmail(JsonElement body)
    {
        string? email = null;
        foreach (var property in body.EnumerateObject())
        {
            // Case-insensitive and last-wins, matching the endpoint's System.Text.Json web binding.
            if (string.Equals(property.Name, EmailProperty, StringComparison.OrdinalIgnoreCase))
            {
                email = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return email;
    }

    private readonly record struct AccountReading(string? Email, string? Sentinel)
    {
        public static AccountReading None => default;

        public static AccountReading For(string? email) => new(email, null);

        public static AccountReading Shared(string sentinel) => new(null, sentinel);
    }
}

public static class LoginRateLimitKeyMiddlewareExtensions
{
    /// <summary>Must run before <c>UseRateLimiter()</c> so both login partitions can read their keys.</summary>
    public static IApplicationBuilder UseLoginRateLimitKeys(this IApplicationBuilder app) =>
        app.UseMiddleware<LoginRateLimitKeyMiddleware>();
}
