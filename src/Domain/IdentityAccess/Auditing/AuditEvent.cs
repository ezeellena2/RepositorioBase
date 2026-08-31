using CleanArchitecture.Domain.IdentityAccess.Tenants;
using System.Collections.ObjectModel;

namespace CleanArchitecture.Domain.IdentityAccess.Auditing;

public sealed class AuditEvent : BaseEntity<Guid>
{
    private AuditEvent()
    {
    }

    public TenantId TenantId { get; private set; }

    public Guid? ActorId { get; private set; }

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
        if (tenantId.Value == Guid.Empty)
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
            EventType = eventType.Trim(),
            CorrelationId = correlationId.Trim(),
            Metadata = new ReadOnlyDictionary<string, string>(copiedMetadata)
        };
    }

    private static Dictionary<string, string> CopyAllowlistedMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var copiedMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return copiedMetadata;
        }

        foreach (var (key, value) in metadata)
        {
            if (key is not ("reason" or "code" or "outcome") || string.IsNullOrWhiteSpace(value) || ContainsSecretMarker(value))
            {
                throw new ArgumentException("Audit metadata must use safe allowlisted scalar fields.", nameof(metadata));
            }

            copiedMetadata.Add(key, value);
        }

        return copiedMetadata;
    }

    private static bool ContainsSecretMarker(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connection string", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase);
}
