using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// The one place a `Personal` context is built, shared by the two ways to reach it: a proved token spent by somebody
/// who had no account, and a session held by somebody who already does. Writing it twice would let the two paths
/// drift, and the whole point of IA-REQ-048 is that they end in the same graph.
/// </summary>
internal static class PersonalContextFactory
{
    /// <summary>
    /// Adds the tenant, the ownership reservation, the responsible membership, the profile and the protected
    /// document to the unit of work. It does not save: the caller owns the transaction, so a failure after any of
    /// these leaves none of them.
    /// </summary>
    internal static void Add(
        IApplicationDbContext context,
        Guid identityId,
        string fullName,
        string displayName,
        string documentCiphertext,
        IReadOnlyList<DocumentFingerprintValue> fingerprints,
        DataClassification classification,
        string correlationId,
        DateTimeOffset now)
    {
        var tenant = Tenant.CreatePersonal(TenantSlug.From($"personal-{identityId:N}"));
        tenant.Activate();

        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        membership.Activate(tenant);

        context.Tenants.Add(tenant);
        context.PersonalTenantOwnerships.Add(PersonalTenantOwnership.Create(tenant, identityId));
        context.TenantMemberships.Add(membership);
        context.PersonProfiles.Add(PersonProfile.Create(tenant, identityId, fullName, displayName, classification));
        context.IdentityDocuments.Add(IdentityDocument.Record(
            identityId,
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            documentCiphertext,
            fingerprints,
            classification,
            now));

        // Identifiers only: the correlation, the tenant and the actor. Neither the document nor the names reach an
        // audit record, which admits three safe keys and would otherwise become the directory it must not be.
        context.AuditEvents.Add(AuditEvent.Create(tenant.Id, identityId, "personal.context.created", correlationId, new Dictionary<string, string>
        {
            ["code"] = "personal.context.created",
            ["outcome"] = "created"
        }));
    }

    /// <summary>
    /// Turns a budget decision into the answer the caller gets. An exhausted budget and an unreachable store are
    /// different facts and get different answers; only an admitted attempt continues.
    /// </summary>
    internal static ApplicationError? Refusal(AttemptBudgetDecision decision) => decision.Outcome switch
    {
        AttemptBudgetOutcome.Admitted => null,
        AttemptBudgetOutcome.Exhausted => IdentityAccessErrors.AttemptsExhausted(RetryAfterSeconds(decision)),
        _ => IdentityAccessErrors.ServiceUnavailable(RetryAfterSeconds(decision))
    };

    private static int RetryAfterSeconds(AttemptBudgetDecision decision) =>
        Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
}
