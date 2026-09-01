using CleanArchitecture.Domain.IdentityAccess.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class OutboxSecretConfiguration : IEntityTypeConfiguration<OutboxSecret>
{
    private const string LifecycleConstraint = "(\"Status\" = 'Pending' AND \"Ciphertext\" IS NOT NULL AND \"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"TerminalReason\" IS NULL AND \"CompletedAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"Status\" = 'Delivered' AND \"Ciphertext\" IS NULL AND \"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"TerminalReason\" IS NULL AND \"CompletedAt\" IS NULL AND \"ProviderReceipt\" IS NOT NULL) OR (\"Status\" = 'Consumed' AND \"Ciphertext\" IS NULL AND \"TerminalReason\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND ((\"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"ProviderReceipt\" IS NOT NULL))) OR (\"Status\" IN ('Expired', 'Failed') AND \"Ciphertext\" IS NULL AND \"TerminalReason\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND ((\"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"ProviderReceipt\" IS NOT NULL)))";

    public void Configure(EntityTypeBuilder<OutboxSecret> builder)
    {
        builder.ToTable("outbox_secrets", table =>
        {
            table.HasCheckConstraint("CK_outbox_secrets_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"OutboxMessageId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_outbox_secrets_Lifecycle", LifecycleConstraint);
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OutboxMessageId).IsUnique();
        builder.HasIndex(x => x.VersionedHash).IsUnique();
        builder.HasOne<OutboxMessage>().WithOne().HasForeignKey<OutboxSecret>(secret => secret.OutboxMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.VersionedHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Ciphertext).HasColumnType("text");
        builder.Property(x => x.DeliveryReason).HasMaxLength(128);
        builder.Property(x => x.DeliveredAt);
        builder.Property(x => x.TerminalReason).HasMaxLength(128);
        builder.Property(x => x.ProviderReceipt).HasMaxLength(256);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
    }
}
