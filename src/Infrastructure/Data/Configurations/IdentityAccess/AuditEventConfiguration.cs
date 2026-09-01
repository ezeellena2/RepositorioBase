using System.Collections.ObjectModel;
using System.Text.Json;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        var converter = new ValueConverter<IReadOnlyDictionary<string, string>, string>(
            metadata => JsonSerializer.Serialize(metadata, (JsonSerializerOptions?)null),
            json => DeserializeMetadata(json));
        var comparer = new ValueComparer<IReadOnlyDictionary<string, string>>(
            (left, right) => MetadataEquals(left, right),
            metadata => MetadataHash(metadata),
            metadata => CopyMetadata(metadata));

        builder.ToTable("AuditEvents", table => table.HasCheckConstraint("CK_AuditEvents_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"TenantId\" IS NULL OR \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"ActorId\" IS NULL OR \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"SessionId\" IS NULL OR \"SessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.Id).ValueGeneratedNever();
        builder.Property(auditEvent => auditEvent.TenantId).HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? TenantId.From(value.Value) : null);
        builder.Property(auditEvent => auditEvent.ActorId);
        builder.Property(auditEvent => auditEvent.SessionId);
        builder.Property(auditEvent => auditEvent.OccurredAt).IsRequired();
        builder.Property(auditEvent => auditEvent.EventType).HasMaxLength(128).IsRequired();
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(auditEvent => auditEvent.Metadata).HasConversion(converter).HasColumnType("jsonb").Metadata.SetValueComparer(comparer);
        builder.HasIndex(auditEvent => auditEvent.TenantId);
        builder.HasIndex(auditEvent => auditEvent.ActorId);
        builder.HasIndex(auditEvent => auditEvent.SessionId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(auditEvent => auditEvent.TenantId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(auditEvent => auditEvent.ActorId).OnDelete(DeleteBehavior.NoAction);
    }

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string json) =>
        new ReadOnlyDictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []);

    private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string> metadata) =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(metadata, StringComparer.Ordinal));

    private static bool MetadataEquals(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value));

    private static int MetadataHash(IReadOnlyDictionary<string, string> metadata) =>
        metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));
}
