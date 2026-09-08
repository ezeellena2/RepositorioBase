namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// The response headers a browser needs in order to keep a session cookie worth anything (IA-REQ-026/027).
/// <para>
/// Every control this system has over authentication ends at the browser. A cookie that cannot be read by script
/// is still readable by script injected into the page that holds it, a session is still stealable by a page that
/// framed this one, and an antiforgery token still travels to whoever a form was retargeted at. None of that is
/// decided by a handler, so none of it can be tested by asking one.
/// </para>
/// <para>
/// The policy is deliberately whole-response rather than per-route: a header a route can forget is a header the
/// next route will. What varies is one exclusion, described where it is taken.
/// </para>
/// </summary>
public static class IdentitySecurityHeaders
{
    /// <summary>
    /// Same-origin everything, no framing, no plugins, no base rewriting, and forms that can only post back here.
    /// <para>
    /// `style-src` allows inline styles because the built client emits them and a policy nobody can deploy is a
    /// policy that gets removed; `script-src` does not, which is the half that matters for injection. `img-src`
    /// allows `data:` for the enrollment QR code, which is generated in the page and never fetched.
    /// </para>
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'";

    /// <summary>
    /// The API reference is a documentation page that loads its own viewer, and it is not the application: it
    /// holds no session, reads no cookie and posts to nothing. It is excluded from the script policy alone —
    /// every other header below still applies to it, framing included.
    /// </summary>
    private static readonly string[] DocumentationPaths = ["/scalar", "/openapi"];

    public static IApplicationBuilder UseIdentitySecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // Written before the response starts rather than after it, because by the time a handler has begun
            // writing a body the headers have already gone.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "DENY";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                // No indexing and no caching of anything under the API: responses there are answers about one
                // caller, and a shared cache holding one is a session handed to the next person through it.
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    headers["Cache-Control"] = "no-store";
                    headers["X-Robots-Tag"] = "noindex, nofollow";
                }

                headers["Content-Security-Policy"] = IsDocumentation(context.Request.Path)
                    ? ContentSecurityPolicy.Replace("script-src 'self'; ", string.Empty, StringComparison.Ordinal)
                    : ContentSecurityPolicy;
                return Task.CompletedTask;
            });

            await next();
        });

    private static bool IsDocumentation(PathString path) =>
        DocumentationPaths.Any(documentation => path.StartsWithSegments(documentation, StringComparison.OrdinalIgnoreCase));
}
