using CleanArchitecture.Domain.IdentityAccess.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint("CK_outbox_messages_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.NextAttemptAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.FirstAttemptAt);
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64);
        builder.Property(x => x.DeliveryLanguage).HasMaxLength(16);
        builder.Property(x => x.TraceId).HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LeaseOwner).HasMaxLength(128);
        builder.Property(x => x.LeaseExpiresAt);
        builder.Property(x => x.DeliveredAt);
        builder.Property(x => x.Generation).IsRequired().IsConcurrencyToken();

        // A terminal message carries the evidence of how it ended and never a lease; a pending one is the only
        // kind a dispatcher may hold. Stating it here means a bad UPDATE is rejected by the database rather than
        // leaving a message that is both delivered and claimed.
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint(
            "CK_outbox_messages_Dispatch",
            "\"AttemptCount\" >= 0 AND \"Generation\" >= 0 AND ((\"LeaseOwner\" IS NULL) = (\"LeaseExpiresAt\" IS NULL)) AND (" +
            "(\"Status\" = 'Pending' AND \"DeliveredAt\" IS NULL) OR " +
            "(\"Status\" = 'Delivered' AND \"DeliveredAt\" IS NOT NULL AND \"LeaseOwner\" IS NULL) OR " +
            "(\"Status\" = 'Abandoned' AND \"DeliveredAt\" IS NULL AND \"LeaseOwner\" IS NULL AND \"FailureCode\" IS NOT NULL))"));
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint(
            "CK_outbox_messages_DeliveryLanguage",
            "\"DeliveryLanguage\" IS NULL OR \"DeliveryLanguage\" IN ('en', 'es')"));

        builder.HasIndex(x => x.NextAttemptAt);

        // The claim query reads due, unleased, still-pending rows only. A partial index keeps that scan on the
        // rows that can actually be claimed instead of on every message the table has ever held.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasFilter("\"Status\" = 'Pending'");
    }
}
