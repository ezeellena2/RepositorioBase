namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

/// <summary>
/// Why a tenant was suspended, from a closed set.
/// <para>
/// The SPEC requires a reason on the suspension and allowlists what the Platform projection may expose. Free text
/// would satisfy the first and quietly break the second: an operator explaining a suspension in prose would put
/// arbitrary content — very plausibly personal data — into a read-only security projection. A code carries the
/// same operational meaning and cannot (IA-REQ-043/044).
/// </para>
/// <para>
/// The set itself is a design choice the SPEC does not fix; extending it is a deliberate change to this enum, the
/// database constraint, and a migration together.
/// </para>
/// </summary>
public enum TenantSuspensionReason
{
    PolicyViolation,
    SecurityIncident,
    BillingHold,
    OperatorRequest
}
