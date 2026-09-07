namespace CleanArchitecture.Domain.IdentityAccess.Identities;

/// <summary>
/// Why a Platform operator stopped an account (IA-REQ-054).
/// <para>
/// A closed set rather than free text, and recorded only in the audit trail — never a column on the account and
/// never in a response. Somebody's reason for being stopped is an operational record about a decision, not an
/// attribute of the person, and an open field on a security projection is where prose about people ends up.
/// </para>
/// </summary>
public enum IdentitySuspensionReason
{
    PolicyViolation,
    SecurityIncident,
    BillingHold,
    OperatorRequest
}
