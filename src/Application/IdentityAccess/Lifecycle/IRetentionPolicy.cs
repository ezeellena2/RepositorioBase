namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>The closed set of things a retention policy can be about (IA-REQ-056).</summary>
public enum RetentionCategory
{
    PersonalProfileNames,
    PersonalIdentityDocument,
    SessionRecords,
    AuditEvents,
    OutboxMessages,
    OutboxSecrets,
    DeliveryEvidence,
    PlatformMfaMaterial
}

/// <summary>What starts the clock on a category.</summary>
public enum RetentionTrigger
{
    RecordCreation,
    LastActivity,
    AccountClosure
}

/// <summary>What happens when it runs out.</summary>
public enum RetentionAction
{
    Retain,
    Anonymise,
    Erase
}

/// <summary>
/// One line of a policy. The period has no default and none is proposed: a number here is a decision somebody
/// with authority made about a jurisdiction, and inventing one in source would be this system deciding it.
/// </summary>
public sealed record RetentionCategoryRule(
    RetentionCategory Category,
    TimeSpan RetentionPeriod,
    RetentionTrigger Trigger,
    RetentionAction Action,
    bool EvidenceRequired);

/// <summary>
/// The policy as a whole, exactly as configured. It names who approved it and where it came from, because a
/// deletion that nobody can trace back to a decision is a deletion nobody authorized.
/// </summary>
public sealed record RetentionPolicyDocument(
    string PolicyId,
    string Version,
    string Owner,
    DateOnly ApprovedOn,
    string Source,
    string BackupTreatment,
    IReadOnlyList<RetentionCategoryRule> Categories);

/// <summary>
/// The configured retention policy, or nothing.
/// <para>
/// "Nothing" is the important case and it is deliberately not an error: a deployment with no policy performs no
/// destructive action, in either personal-data mode. That is why this is a nullable read rather than a throw —
/// the absence is a state the system runs in, not a misconfiguration to refuse.
/// </para>
/// <para>
/// A policy that is present but unreadable is also nothing. Guessing at half-parsed retention instructions is the
/// one way this could delete something nobody asked it to (IA-REQ-056).
/// </para>
/// </summary>
public interface IRetentionPolicy
{
    RetentionPolicyDocument? Current { get; }
}
