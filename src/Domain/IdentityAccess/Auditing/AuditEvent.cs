using CleanArchitecture.Domain.IdentityAccess.Tenants;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace CleanArchitecture.Domain.IdentityAccess.Auditing;

public sealed class AuditEvent : BaseEntity<Guid>
{
    private AuditEvent()
    {
    }

    public TenantId? TenantId { get; private set; }

    public Guid? ActorId { get; private set; }

    public Guid? SessionId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public static AuditEvent Create(
        TenantId tenantId,
        Guid? actorId,
        string eventType,
        string correlationId,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type cannot be empty.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        }

        var copiedMetadata = CopyAllowlistedMetadata(metadata);
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = eventType.Trim(),
            CorrelationId = correlationId.Trim(),
            Metadata = new ReadOnlyDictionary<string, string>(copiedMetadata)
        };
    }

    /// <summary>
    /// Records a denial independently of a tenant-owned resource. The value object only
    /// accepts the safe evidence contract for authorization denials.
    /// </summary>
    public static AuditEvent CreateAuthorizationDenied(
        TenantId? tenantId,
        Guid? actorId,
        Guid? sessionId,
        string correlationId,
        string permissionCode,
        string outcome,
        DateTimeOffset occurredAt)
    {
        if (actorId == Guid.Empty || sessionId == Guid.Empty || tenantId is { IsEmpty: true })
        {
            throw new ArgumentException("Authorization denial identifiers cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(correlationId) || !IsStableMachineIdentifier(permissionCode) || !IsStableMachineIdentifier(outcome))
        {
            throw new ArgumentException("Authorization denials require safe correlation, permission, and outcome values.");
        }

        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            SessionId = sessionId,
            OccurredAt = occurredAt,
            EventType = "authorization.denied",
            CorrelationId = correlationId.Trim(),
            Metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = permissionCode.Trim(),
                ["outcome"] = outcome.Trim()
            })
        };
    }

    public static AuditEvent CreateIdentityConfirmed(Guid identityId, TenantId? tenantId, string correlationId, DateTimeOffset occurredAt)
    {
        if (identityId == Guid.Empty || tenantId is { IsEmpty: true } || string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Identity confirmation audit evidence is invalid.");
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorId = identityId,
            TenantId = tenantId,
            OccurredAt = occurredAt,
            EventType = "identity.confirmed",
            CorrelationId = correlationId,
            Metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["code"] = "identity.confirmed",
                ["outcome"] = "activated"
            })
        };
    }

    public static AuditEvent CreateMembershipChanged(TenantId tenantId, Guid? actorId, string correlationId, string outcome = "changed") =>
        Create(tenantId, actorId, "membership.changed", correlationId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "membership.changed",
            ["outcome"] = outcome
        });

    public static AuditEvent CreateSessionEvent(Guid? actorId, Guid? sessionId, string eventType, string correlationId, string outcome)
    {
        if (actorId == Guid.Empty || sessionId == Guid.Empty || string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(correlationId) || !IsStableMachineIdentifier(eventType) || !IsStableMachineIdentifier(outcome))
            throw new ArgumentException("Session audit evidence is invalid.");

        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            SessionId = sessionId,
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = eventType,
            CorrelationId = correlationId,
            Metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = eventType,
                ["outcome"] = outcome
            })
        };
    }

    public static AuditEvent CreateRoleChanged(TenantId tenantId, Guid? actorId, string correlationId, string outcome = "changed") =>
        Create(tenantId, actorId, "role.changed", correlationId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "role.changed",
            ["outcome"] = outcome
        });

    private static Dictionary<string, string> CopyAllowlistedMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var copiedMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return copiedMetadata;
        }

        foreach (var (key, value) in metadata)
        {
            if (!IsSafeMetadataValue(key, value))
            {
                throw new ArgumentException("Audit metadata must use safe allowlisted scalar fields.", nameof(metadata));
            }

            copiedMetadata.Add(key, value);
        }

        return copiedMetadata;
    }

    private static bool IsSafeMetadataValue(string key, string value)
    {
        if (key is not ("reason" or "code" or "outcome") || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (ContainsSecretMaterial(value))
        {
            return false;
        }

        return key switch
        {
            "reason" => true,
            "code" or "outcome" => IsStableMachineIdentifier(value),
            _ => false
        };
    }

    private static bool IsStableMachineIdentifier(string value) =>
        value.Any(char.IsAsciiLetter) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool ContainsSecretMaterial(string value) =>
        ContainsDangerousMarker(value) || ContainsVerificationCodeMaterial(value);

    private static bool ContainsDangerousMarker(string value) =>
        Regex.IsMatch(
            value,
            @"\b(?:password|token|cookie|secret|connection\s*string|connectionstring)\b\s*[:=]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsVerificationCodeMaterial(string value) =>
        Regex.IsMatch(
            value,
            @"\b(?:otp|one[\s_-]?time|recovery|confirmation|verification)(?=$|[\s_-])[\s_-]*(?:code|pin)?[\s_:-]*(?:\d{4,10}|(?=[A-Za-z0-9]{6,}\b)(?=[A-Za-z0-9]*[A-Za-z])(?=[A-Za-z0-9]*\d)[A-Za-z0-9]{6,})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
