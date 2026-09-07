using CleanArchitecture.Application.IdentityAccess.Lifecycle;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Refuses public ingress while admission is closed (IA-REQ-055).
/// <para>
/// It runs before authentication, because a closed deployment must not read a cookie, look up a session or touch
/// the restored database at all — deciding to refuse *after* consulting the data that may itself be restored
/// would be the thing this exists to prevent.
/// </para>
/// <para>
/// The only exceptions are the liveness and readiness probes, which answer with a status and no body. An
/// orchestrator has to be able to tell a process that is running and refusing from one that is not running, and a
/// probe that returned anything about the deployment would be ingress.
/// </para>
/// </summary>
public sealed class RecoveryAdmissionMiddleware(RequestDelegate next, IRecoveryAdmission admission)
{
    /// <summary>How long a caller is asked to wait. A **product default**: an operator's release is not a poll.</summary>
    private const int RetryAfterSeconds = 60;

    /// <summary>The two paths that answer while everything else does not, and they answer with nothing.</summary>
    private static readonly string[] Probes = ["/health", "/alive"];

    /// <summary>
    /// What "authentication and revalidation" means as a set of paths (IA-REQ-055).
    /// <para>
    /// Each of these lets somebody establish or re-establish who they are and reads nothing else. Deliberately
    /// absent: `/api/identity/context`, which reports tenants and permissions read from restored rows; the
    /// password recovery pair, which mints a token from restored state; and the provider callback, which would
    /// turn a restored external link into a session. Being able to sign in is the point of quarantine; being able
    /// to see what the backup said is not.
    /// </para>
    /// <para>
    /// It is a path allowlist rather than endpoint metadata because this middleware runs before routing, and it
    /// runs before routing because a deployment that has not been admitted must not read the database to find out
    /// what it is refusing.
    /// </para>
    /// </summary>
    private static readonly string[] Authentication =
    [
        "/api/identity/antiforgery",
        "/api/identity/sessions",
        "/api/identity/credentials/reauthenticate"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var current = admission.Current;
        if (IsProbe(context.Request.Path) ||
            (current.AdmitsPublicIngress && !(current.AdmitsOnlyAuthentication && !IsAuthentication(context.Request.Path))))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.ContentType = "application/problem+json";

        // Written directly rather than through the problem-details mapper, which resolves services from the
        // request scope. A closed deployment answers without building anything that reads the database.
        await context.Response.WriteAsync(
            """
            {"type":"about:blank","title":"Service Unavailable","status":503,"detail":"This deployment is not admitting requests.","code":"recovery_admission_closed","retryAfterSeconds":60}
            """);
    }

    private static bool IsProbe(PathString path) =>
        Probes.Any(probe => path.Equals(probe, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Prefix matching, so `/api/identity/sessions/current` travels with `/api/identity/sessions`: signing out is
    /// as much a part of establishing who you are as signing in.
    /// </summary>
    private static bool IsAuthentication(PathString path) =>
        Authentication.Any(route => path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase));
}
