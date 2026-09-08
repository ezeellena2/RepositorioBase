using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Transport rate limiting for the login endpoint (IA-REQ-019): two chained fixed windows over the shared attempt
/// store, per client address and per normalized account. Identity lockout is a separate control owned by the
/// account service; both stay distinct on purpose.
/// <para>
/// The budgets live in PostgreSQL rather than in this process. An in-memory limiter is not a limit for a
/// deployment that is more than one process: an attacker escapes it by reaching the other instance, and a restart
/// refunds every attempt anybody had spent. Neither weakness is visible from inside one process, which is why the
/// evidence for this lives in tests that build two hosts and dispose one (IA-REQ-057).
/// </para>
/// </summary>
public static class LoginRateLimiting
{
    public const int ClientPermitLimit = 20;
    public const int AccountPermitLimit = 10;
    public static readonly TimeSpan ClientWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AccountWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The two budgets, named. The scopes are separate so one address spending its budget cannot collide with an
    /// account's, and the numbers are a **product choice** — the SPEC fixes none — set high enough that a person
    /// mistyping a password is not locked out and low enough that a password list is not walked through this route.
    /// </summary>
    public static readonly AttemptBudget ClientBudget = new("identity.login.client", ClientPermitLimit, ClientWindow);

    public static readonly AttemptBudget AccountBudget = new("identity.login.account", AccountPermitLimit, AccountWindow);

    public static IApplicationBuilder UseLoginAttemptBudgets(this IApplicationBuilder app) =>
        app.UseMiddleware<LoginAttemptBudgetMiddleware>();

    /// <summary>
    /// Declares that a route is bounded by the login budgets. Endpoint metadata rather than a path comparison,
    /// so the route stays the router's to name and nothing has to be kept in step with a string.
    /// </summary>
    public static RouteHandlerBuilder RequireLoginAttemptBudgets(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(LoginAttemptBudgetMetadata.Instance);
}

/// <summary>The marker <see cref="LoginRateLimiting.RequireLoginAttemptBudgets"/> puts on the login route.</summary>
public sealed class LoginAttemptBudgetMetadata
{
    public static readonly LoginAttemptBudgetMetadata Instance = new();

    private LoginAttemptBudgetMetadata()
    {
    }
}

/// <summary>
/// Spends the login budgets before the endpoint sees the request, so a refused attempt never reaches a password
/// hash, an audit record or a session (IA-REQ-019).
/// </summary>
public sealed class LoginAttemptBudgetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISharedAttemptBudget budgets, IProblemDetailsService problems)
    {
        if (!LoginRateLimitKeyMiddleware.IsLoginEndpoint(context))
        {
            await next(context);
            return;
        }

        // The client address first, and the account only if the address was admitted. Spending the account budget
        // for a caller who is already over their own would let one address lock out any account it can name.
        if (context.Items[LoginRateLimitKeyMiddleware.ClientKeyItem] is string clientKey &&
            await RefusedAsync(context, budgets, problems, LoginRateLimiting.ClientBudget, clientKey))
        {
            return;
        }

        if (context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem] is string accountKey &&
            await RefusedAsync(context, budgets, problems, LoginRateLimiting.AccountBudget, accountKey))
        {
            return;
        }

        await next(context);
    }

    private static async Task<bool> RefusedAsync(
        HttpContext context,
        ISharedAttemptBudget budgets,
        IProblemDetailsService problems,
        AttemptBudget budget,
        string key)
    {
        var decision = await budgets.SpendAsync(budget, key, context.RequestAborted);
        if (decision.IsAdmitted) return false;

        // The contract requires Retry-After on both refusals, and the two are deliberately different answers: a
        // caller who really spent their attempts is told so, and an outage is told as an outage rather than as
        // something the caller did (IA-REQ-057).
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        var error = decision.Outcome == AttemptBudgetOutcome.Unavailable
            ? new ApplicationError(ServiceUnavailableCode, ApplicationErrorCategory.Unavailable, retryAfterSeconds: retryAfterSeconds)
            : new ApplicationError(RateLimitExceededCode, ApplicationErrorCategory.RateLimited, retryAfterSeconds: retryAfterSeconds);
        await problems.WriteAsync(context, error, context.RequestAborted);
        return true;
    }

    private const string RateLimitExceededCode = "rate_limit_exceeded";
    private const string ServiceUnavailableCode = "service_unavailable";
}
