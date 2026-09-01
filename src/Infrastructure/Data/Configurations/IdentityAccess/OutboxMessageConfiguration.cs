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
        builder.HasIndex(x => x.NextAttemptAt);
    }
}
