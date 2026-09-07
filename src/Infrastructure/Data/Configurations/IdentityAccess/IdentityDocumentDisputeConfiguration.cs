using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class IdentityDocumentDisputeConfiguration : IEntityTypeConfiguration<IdentityDocumentDispute>
{
    public void Configure(EntityTypeBuilder<IdentityDocumentDispute> builder)
    {
        builder.ToTable("IdentityDocumentDisputes", table =>
        {
            table.HasCheckConstraint(
                "CK_IdentityDocumentDisputes_Ids_NotEmpty",
                "\"DisputeId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // A settled dispute and the moment it settled are one fact, and a reason code is a short stable
            // identifier — never a place for anything about the person.
            table.HasCheckConstraint(
                "CK_IdentityDocumentDisputes_Lifecycle",
                "\"Version\" > 0 AND (\"Status\" = 'Open') = (\"ResolvedAt\" IS NULL) AND " +
                "(\"ResolvedAt\" IS NULL OR \"ResolvedAt\" >= \"OpenedAt\") AND " +
                "\"ReasonCode\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND length(\"ClaimedCiphertext\") > 0");
        });

        builder.HasKey(dispute => dispute.DisputeId);
        builder.Property(dispute => dispute.DisputeId).ValueGeneratedNever();
        builder.Property(dispute => dispute.SubjectIdentityId).IsRequired();
        builder.Property(dispute => dispute.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(dispute => dispute.ClaimedCiphertext).HasMaxLength(2048).IsRequired();
        builder.Property(dispute => dispute.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(dispute => dispute.OpenedAt).IsRequired();
        builder.Property(dispute => dispute.ResolvedAt);
        builder.Property(dispute => dispute.Version).IsConcurrencyToken().IsRequired();

        // At most one dispute per identity is open at a time, decided here rather than by whoever reads first.
        builder.HasIndex(dispute => dispute.SubjectIdentityId).IsUnique().HasFilter("\"Status\" = 'Open'");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(dispute => dispute.SubjectIdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IdentityDocumentCorrectionRecordConfiguration : IEntityTypeConfiguration<IdentityDocumentCorrectionRecord>
{
    public void Configure(EntityTypeBuilder<IdentityDocumentCorrectionRecord> builder)
    {
        builder.ToTable("IdentityDocumentCorrectionRecords", table =>
        {
            table.HasCheckConstraint(
                "CK_IdentityDocumentCorrectionRecords_Ids_NotEmpty",
                "\"RecordId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DisputeId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ResolvedByMembershipId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // The evidence reference names a case held outside this system. Its shape is enforced here as well
            // as in the aggregate, because this row is what an auditor reads.
            table.HasCheckConstraint(
                "CK_IdentityDocumentCorrectionRecords_Evidence",
                "\"EvidenceReference\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND \"PreviousKeyVersions\" ~ '^[0-9,]*$'");
        });

        builder.HasKey(record => record.RecordId);
        builder.Property(record => record.RecordId).ValueGeneratedNever();
        builder.Property(record => record.SubjectIdentityId).IsRequired();
        builder.Property(record => record.DisputeId).IsRequired();
        builder.Property(record => record.ResolvedAt).IsRequired();
        builder.Property(record => record.ResolvedByMembershipId).IsRequired();
        builder.Property(record => record.EvidenceReference).HasMaxLength(64).IsRequired();
        builder.Property(record => record.PreviousKeyVersions).HasMaxLength(128).IsRequired();

        builder.HasIndex(record => record.SubjectIdentityId);
        builder.HasIndex(record => record.DisputeId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(record => record.SubjectIdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}
