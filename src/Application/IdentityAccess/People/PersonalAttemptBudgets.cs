using CleanArchitecture.Application.IdentityAccess.Security;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// The budgets a person's own context spends. The numbers are **product defaults**, not legal or external-standard
/// requirements; Task 27 is where they move to per-environment configuration.
/// </summary>
public static class PersonalAttemptBudgets
{
    /// <summary>
    /// Bounds how often one identity may claim a documentary identity. Recording a document answers the same refusal
    /// whether the number is taken or the caller already owns a Personal context, so without a budget that one answer
    /// could still be asked indefinitely (SPEC section 14.3).
    /// </summary>
    public static readonly AttemptBudget DocumentClaim = new("personal.document.claim", 3, TimeSpan.FromHours(24));
}
