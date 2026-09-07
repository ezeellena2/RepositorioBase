using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// A provider that really speaks the protocol, standing where Google stands.
/// <para>
/// It exists so the tests exercise the framework's own handler rather than a stub of the final identity: it
/// publishes a discovery document and a JWKS, mints authorization codes bound to the PKCE challenge and the nonce
/// the handler chose, and refuses a code exchange whose verifier, client credentials or redirect do not match. A
/// double that returned a ready-made principal would prove none of that, and every negative below — wrong issuer,
/// wrong audience, wrong signature, expired, replayed — would be untestable.
/// </para>
/// </summary>
internal sealed class ControlledOidcProvider : IDisposable
{
    internal const string Issuer = "https://oidc.test";
    internal const string ClientId = "controlled-client-id";
    internal const string ClientSecret = "controlled-client-secret";
    private const string KeyId = "controlled-signing-key";

    private readonly RSA _signing = RSA.Create(2048);

    /// <summary>Never published in the JWKS, so anything it signs is a signature nothing can validate.</summary>
    private readonly RSA _unpublished = RSA.Create(2048);

    private readonly Dictionary<string, PendingCode> _codes = new(StringComparer.Ordinal);

    /// <summary>What the id_token will claim to be from. A test sets it to prove the issuer is really checked.</summary>
    internal string IssuerClaim { get; set; } = Issuer;

    internal string AudienceClaim { get; set; } = ClientId;

    internal TimeSpan IdTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Signs with the key the JWKS does not carry.</summary>
    internal bool SignWithUnpublishedKey { get; set; }

    /// <summary>Replaces the nonce the handler asked for, which is the replay control the protocol turns on.</summary>
    internal string? NonceOverride { get; set; }

    /// <summary>
    /// Signed authentication evidence. It defaults to "just now" because that is what a conformant provider
    /// answers: OIDC Core 1.0 section 3.1.2.1 makes `auth_time` REQUIRED in the ID token once the request carried
    /// `max_age`, which every proof challenge now does. Assigning `null` models a provider that answers without
    /// it, and any other value models one that answers with something a caller must not simply believe — token
    /// issuance itself proves no authentication time.
    /// </summary>
    internal object? AuthenticationTimeClaim { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    internal int TokenExchanges { get; private set; }

    internal HttpMessageHandler CreateHandler() => new ProviderHandler(this);

    /// <summary>
    /// Mints a code for an authorization request the handler actually produced, binding it to that request's
    /// nonce, PKCE challenge and redirect so the exchange can only succeed for the browser that started it.
    /// </summary>
    internal string IssueCode(string nonce, string codeChallenge, string redirectUri, string subject, string? email, bool emailVerified)
    {
        var code = $"code-{Guid.NewGuid():N}";
        _codes[code] = new PendingCode(nonce, codeChallenge, redirectUri, subject, email, emailVerified);
        return code;
    }

    public void Dispose()
    {
        _signing.Dispose();
        _unpublished.Dispose();
    }

    private string Discovery() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["issuer"] = Issuer,
        ["authorization_endpoint"] = $"{Issuer}/authorize",
        ["token_endpoint"] = $"{Issuer}/token",
        ["jwks_uri"] = $"{Issuer}/jwks",
        ["response_types_supported"] = new[] { "code" },
        ["response_modes_supported"] = new[] { "form_post", "query" },
        ["subject_types_supported"] = new[] { "public" },
        ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
        ["scopes_supported"] = new[] { "openid", "email" },
        ["grant_types_supported"] = new[] { "authorization_code" },
        ["code_challenge_methods_supported"] = new[] { "S256" },
        ["claims_supported"] = new[] { "sub", "email", "email_verified", "aud", "exp", "iat", "iss" }
    });

    private string Jwks()
    {
        var parameters = _signing.ExportParameters(false);
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["keys"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = SecurityAlgorithms.RsaSha256,
                    ["kid"] = KeyId,
                    ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
                    ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!)
                }
            }
        });
    }

    private async Task<HttpResponseMessage> ExchangeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TokenExchanges++;
        var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));

        if (Get(form, "grant_type") != "authorization_code") return Error("unsupported_grant_type");
        if (Get(form, "client_id") != ClientId || Get(form, "client_secret") != ClientSecret) return Error("invalid_client");

        var code = Get(form, "code");

        // One use, and removed before anything else can fail: a replayed code must be dead even if the retry
        // would otherwise have been well formed.
        if (code is null || !_codes.Remove(code, out var pending)) return Error("invalid_grant");
        if (Get(form, "redirect_uri") != pending.RedirectUri) return Error("invalid_grant");

        // PKCE, verified rather than assumed. Without this the tests would pass for a handler that sent no
        // verifier at all, which is exactly the control being claimed.
        var verifier = Get(form, "code_verifier");
        if (verifier is null || Challenge(verifier) != pending.CodeChallenge) return Error("invalid_grant");

        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = pending.Subject,
            ["nonce"] = NonceOverride ?? pending.Nonce,
            ["email_verified"] = pending.EmailVerified
        };
        if (pending.Email is not null) claims["email"] = pending.Email;
        if (AuthenticationTimeClaim is not null) claims["auth_time"] = AuthenticationTimeClaim;

        var key = new RsaSecurityKey(SignWithUnpublishedKey ? _unpublished : _signing) { KeyId = KeyId };
        var idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = IssuerClaim,
            Audience = AudienceClaim,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(IdTokenLifetime),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });

        return Json(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["access_token"] = $"access-{Guid.NewGuid():N}",
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["id_token"] = idToken
        }));
    }

    internal static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty, StringComparer.Ordinal);

    private static string? Get(Dictionary<string, string> form, string name) => form.TryGetValue(name, out var value) ? value : null;

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Error(string code) => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent($"{{\"error\":\"{code}\"}}", Encoding.UTF8, "application/json")
    };

    private sealed record PendingCode(string Nonce, string CodeChallenge, string RedirectUri, string Subject, string? Email, bool EmailVerified);

    private sealed class ProviderHandler(ControlledOidcProvider provider) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => Json(provider.Discovery()),
                "/jwks" => Json(provider.Jwks()),
                "/token" => await provider.ExchangeAsync(request, cancellationToken),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
    }
}
