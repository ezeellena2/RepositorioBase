using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// The Platform budgets, named once so a handler and a test cannot drift apart on what the number is.
/// <para>
/// Both live in the shared store rather than in the process that answered. A budget held in a field is escaped by
/// reaching another instance and refunded by a restart, and neither weakness can be seen from inside one process
/// — which is exactly why both survived as long as they did (IA-REQ-057).
/// </para>
/// </summary>
public static class PlatformAttemptBudgets
{
    /// <summary>
    /// Second-factor submissions per identity: enrollment verification, step-up and recovery share it, because
    /// they accept the same secret and bounding one would only move the guessing to the others (IA-REQ-041).
    /// <para>
    /// Five in fifteen minutes. The SPEC fixes no number, so this is a **product choice**: enough that an
    /// administrator mistyping a code, or one whose authenticator clock has drifted a window, is not locked out of
    /// their own console; few enough that a six-digit code is not found by persistence. An accepted code clears
    /// it, so only failures accumulate.
    /// </para>
    /// <para>
    /// Keyed on the identity behind the validated session — never the session, which a caller replaces with one
    /// request, and never anything they submit.
    /// </para>
    /// </summary>
    public static readonly AttemptBudget MfaAttempt = new("platform.mfa.attempt", 5, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Bootstrap recovery attempts (IA-REQ-040), keyed on the pending invitation and the request's transport
    /// source — both derived, because the request is bodyless precisely so there is nothing for a caller to vary.
    /// Five in fifteen minutes: enough for an operator retrying a failed delivery, few enough that the route
    /// cannot be used to hammer the outbox.
    /// </summary>
    public static readonly AttemptBudget BootstrapRecovery = new("platform.bootstrap.recovery", 5, TimeSpan.FromMinutes(15));

    /// <summary>The key the second-factor budget is spent against. One place, so two callers cannot spell it differently.</summary>
    public static string MfaKey(Guid identityId) => identityId.ToString("N");

    /// <summary>
    /// What a second-factor refusal is told to the caller, or null when there was none. The two refusals are
    /// different answers on purpose: an exhausted budget is something the caller did, and an unreachable store is
    /// not — telling somebody who spent nothing that they spent everything is a lie, and it hides the outage from
    /// whoever is watching (IA-REQ-057, amendment A5).
    /// </summary>
    public static ApplicationError? MfaRefusal(AttemptBudgetDecision decision) => decision.Outcome switch
    {
        AttemptBudgetOutcome.Admitted => null,
        AttemptBudgetOutcome.Exhausted => IdentityAccessErrors.MfaAttemptsExhausted(RetryAfterSeconds(decision)),
        _ => IdentityAccessErrors.ServiceUnavailable(RetryAfterSeconds(decision))
    };

    public static ApplicationError? RecoveryRefusal(AttemptBudgetDecision decision) => decision.Outcome switch
    {
        AttemptBudgetOutcome.Admitted => null,
        AttemptBudgetOutcome.Exhausted => new ApplicationError(
            "rate_limit_exceeded",
            ApplicationErrorCategory.RateLimited,
            "Too many recovery attempts. Try again later.",
            retryAfterSeconds: RetryAfterSeconds(decision)),
        _ => IdentityAccessErrors.ServiceUnavailable(RetryAfterSeconds(decision))
    };

    /// <summary>Retry-After has to be positive to be an answer at all; a window that has just turned rounds to one.</summary>
    private static int RetryAfterSeconds(AttemptBudgetDecision decision) =>
        Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
}
