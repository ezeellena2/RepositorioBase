using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// The provider round trip, configured rather than written.
/// <para>
/// Every protocol control — state, nonce, PKCE, the code exchange, signature, issuer, audience and expiry
/// validation — belongs to ASP.NET Core's OpenID Connect handler. Nothing in this feature implements OAuth or
/// cryptography of its own; what is written here is which handoff a callback belongs to, which is the one thing
/// the protocol does not carry.
/// </para>
/// </summary>
internal static class GoogleOidcConfiguration
{
    private const string Section = "IdentityAccess:ExternalLogins:Google";

    /// <summary>Google's own issuer. A deployment overrides it only so a test can stand a controlled one up.</summary>
    private const string DefaultAuthority = "https://accounts.google.com";

    /// <summary>
    /// The one route the provider itself calls back on. It is a literal because a scheme has exactly one, and it
    /// is the URI a deployment registers with the provider, so it must never move.
    /// </summary>
    internal const string CallbackPath = "/api/identity/external/google/callback";

    /// <summary>Where the provider leg ends. A fixed, local, allowlisted path — never anything the callback said.</summary>
    private const string ReturnPath = "/external/return";

    /// <summary>
    /// The one refusal slug. The closed set the redirect may carry is `signed_in`, `linked`, `proved` and this;
    /// every way the round trip can fail — a state that will not unprotect, a signature, an issuer, an audience,
    /// an expiry, a nonce, a person who declined — arrives as the same word, because which of them it was is not
    /// something the address bar should say.
    /// </summary>
    private const string Refused = "refused";

    /// <summary>The item the challenge carries through the protocol's own protected state.</summary>
    private const string HandoffItem = "identity-access.handoff";

    internal static void AddGoogleExternalLogin(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IExternalHandoffContext, ExternalHandoffContext>();

        var clientId = builder.Configuration[$"{Section}:ClientId"];
        var clientSecret = builder.Configuration[$"{Section}:ClientSecret"];

        // No registration, no middleware. A deployment that has not been given a client stays a deployment where
        // the provider is simply not on offer, rather than one with a route that fails late.
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return;

        var authority = builder.Configuration[$"{Section}:Authority"];
        builder.Services.AddAuthentication().AddOpenIdConnect(ExternalProviders.Google, options =>
        {
            options.Authority = string.IsNullOrWhiteSpace(authority) ? DefaultAuthority : authority;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.CallbackPath = CallbackPath;

            // Authorization code with PKCE, exchanged from the server. The browser never holds a provider token,
            // and none is persisted afterwards: this system's own session is the only thing it keeps.
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.FormPost;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");

            // Unmapped, so the claims are read under the names the specification gives them rather than under
            // whichever URI the inbound mapper would have chosen.
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.NameClaimType = "sub";

            options.Events = new OpenIdConnectEvents
            {
                OnTicketReceived = OnTicketReceived,
                OnRemoteFailure = OnRemoteFailure,
                OnAccessDenied = OnAccessDenied
            };
        });
    }

    /// <summary>
    /// Starts the provider leg for the handoff the browser's own cookie names. The identifier travels onward in
    /// the handler's protected properties, so what comes back names a handoff this server chose.
    /// <para>
    /// A `Proof` asks the provider for `prompt=login`. Without it the provider answers from whatever session the
    /// browser already holds there, so the round trip would prove possession of an unlocked device and nothing
    /// about the person — and a recent identity proof that proves no presence is not a proof (IA-REQ-051).
    /// </para>
    /// </summary>
    internal static IResult Challenge(HttpContext context, Guid handoffId, ExternalAuthorizationPurpose purpose)
    {
        var properties = new OpenIdConnectChallengeProperties { RedirectUri = ReturnPath };
        if (purpose == ExternalAuthorizationPurpose.Proof) properties.Prompt = "login";
        properties.Items[HandoffItem] = handoffId.ToString("N");
        return Results.Challenge(properties, [ExternalProviders.Google]);
    }

    private static async Task OnTicketReceived(TicketReceivedContext context)
    {
        // Handled here and nowhere else: no external sign-in cookie is written, because a validated assertion is
        // not yet a session. What it becomes is decided by the authorized request the person makes next.
        context.HandleResponse();

        var handoffId = ReadHandoff(context.Properties);
        if (handoffId is null)
        {
            context.Response.Redirect($"{ReturnPath}?outcome={Refused}");
            return;
        }

        var principal = context.Principal;
        var subject = principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            await RejectAsync(context.HttpContext, handoffId.Value);
            context.Response.Redirect($"{ReturnPath}?outcome={Refused}");
            return;
        }

        var email = principal!.FindFirst("email")?.Value;
        var emailVerified = string.Equals(principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        var recorder = context.HttpContext.RequestServices.GetRequiredService<IExternalCallbackRecorder>();
        var outcome = await recorder.RecordAsync(handoffId.Value, ExternalProviders.Google, subject, email, emailVerified, context.HttpContext.RequestAborted);
        if (outcome is null)
        {
            context.Response.Redirect($"{ReturnPath}?outcome={Refused}");
            return;
        }

        // Re-sealed rather than left standing, so the cookie the next request presents is the one this callback
        // wrote and expires on its own even if that request never comes.
        ExternalHandoffCookie.Seal(context.HttpContext, handoffId.Value, outcome.Purpose);
        context.Response.Redirect($"{ReturnPath}?outcome={Slug(outcome.Purpose)}");
    }

    /// <summary>
    /// Everything the provider leg can fail as, answered as one clean local redirect. The failure carries the
    /// callback's own parameters, so neither the exception nor the request is logged or reflected.
    /// </summary>
    private static async Task OnRemoteFailure(RemoteFailureContext context)
    {
        context.HandleResponse();

        // The cookie is cleared only for a failure that names a handoff. Anyone can post nonsense to the callback
        // path, and clearing unconditionally would let them delete the handoff a person is in the middle of.
        if (ReadHandoff(context.Properties) is { } handoffId)
        {
            await RejectAsync(context.HttpContext, handoffId);
            ExternalHandoffCookie.Clear(context.HttpContext);
        }

        context.Response.Redirect($"{ReturnPath}?outcome={Refused}");
    }

    private static async Task OnAccessDenied(AccessDeniedContext context)
    {
        context.HandleResponse();
        if (ReadHandoff(context.Properties) is { } handoffId)
        {
            await RejectAsync(context.HttpContext, handoffId);
            ExternalHandoffCookie.Clear(context.HttpContext);
        }

        context.Response.Redirect($"{ReturnPath}?outcome={Refused}");
    }

    private static Task RejectAsync(HttpContext context, Guid handoffId) =>
        context.RequestServices.GetRequiredService<IExternalCallbackRecorder>().RejectAsync(handoffId, context.RequestAborted);

    private static Guid? ReadHandoff(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(HandoffItem, out var value) == true && Guid.TryParseExact(value, "N", out var handoffId)
            ? handoffId
            : null;

    private static string Slug(ExternalAuthorizationPurpose purpose) => purpose switch
    {
        ExternalAuthorizationPurpose.Login => "signed_in",
        ExternalAuthorizationPurpose.Link => "linked",
        _ => "proved"
    };
}

/// <summary>
/// The short-lived cookie that says which handoff this browser is carrying.
/// <para>
/// It is the answer to a question the callback cannot be trusted with. A caller naming its own handoff in a
/// parameter could name somebody else's; a cookie this server sealed cannot be chosen by the caller at all, and
/// the time limit means an abandoned round trip stops being usable without anything having to clean it up.
/// </para>
/// </summary>
internal static class ExternalHandoffCookie
{
    internal const string Name = "__Host-ia-external";

    private const string Purpose = "identity-access.external.handoff.v1";

    /// <summary>Matches the handoff's own lifetime, so the cookie can never outlive what it names.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    internal static void Seal(HttpContext context, Guid handoffId, ExternalAuthorizationPurpose? purpose = null) =>
        context.Response.Cookies.Append(
            Name,
            Protector(context).Protect($"{handoffId:N}:{(purpose is null ? string.Empty : purpose.Value.ToString())}", Lifetime),
            Options());

    internal static (Guid Handoff, ExternalAuthorizationPurpose? Purpose)? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Name, out var envelope) || string.IsNullOrWhiteSpace(envelope)) return null;
        try
        {
            var opened = Protector(context).Unprotect(envelope).Split(':', 2);
            if (!Guid.TryParseExact(opened[0], "N", out var handoffId)) return null;
            return (handoffId, Enum.TryParse<ExternalAuthorizationPurpose>(opened.ElementAtOrDefault(1), out var purpose) ? purpose : null);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, tampered with, or sealed by a key ring this process no longer has. All three mean the
            // same thing to a caller: this browser is not carrying a handoff.
            return null;
        }
    }

    internal static void Clear(HttpContext context) => context.Response.Cookies.Delete(Name, Options());

    private static CookieOptions Options() => new()
    {
        Path = "/",
        Secure = true,
        HttpOnly = true,
        // The provider returns by a cross-site form post, which is the whole shape of the protocol's return leg;
        // a stricter value would drop the cookie on exactly the request it exists for.
        SameSite = SameSiteMode.None
    };

    private static ITimeLimitedDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).ToTimeLimitedDataProtector();
}

internal sealed class ExternalHandoffContext(IHttpContextAccessor accessor, IConfiguration configuration) : IExternalHandoffContext
{
    public Guid? Current => Sealed()?.Handoff;

    public ExternalAuthorizationPurpose? CurrentPurpose => Sealed()?.Purpose;

    private (Guid Handoff, ExternalAuthorizationPurpose? Purpose)? Sealed() =>
        accessor.HttpContext is { } context ? ExternalHandoffCookie.Read(context) : null;

    public bool IsConfigured(string provider) =>
        provider == ExternalProviders.Google
        && !string.IsNullOrWhiteSpace(configuration["IdentityAccess:ExternalLogins:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["IdentityAccess:ExternalLogins:Google:ClientSecret"]);

    public IReadOnlyList<string> Configured =>
        IsConfigured(ExternalProviders.Google) ? [ExternalProviders.Google] : [];
}
