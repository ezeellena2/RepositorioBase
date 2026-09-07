using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class PersonalDataErasureRecordConfiguration : IEntityTypeConfiguration<PersonalDataErasureRecord>
{
    public void Configure(EntityTypeBuilder<PersonalDataErasureRecord> builder)
    {
        builder.ToTable("PersonalDataErasureRecords", table =>
        {
            table.HasCheckConstraint(
                "CK_PersonalDataErasureRecords_Ids_NotEmpty",
                "\"RecordId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // A record of nothing having happened is not evidence of an erasure, and a deletion nobody can trace
            // to a policy is a deletion nobody authorized.
            table.HasCheckConstraint(
                "CK_PersonalDataErasureRecords_Evidence",
                "\"AffectedRowCount\" > 0 AND length(\"PolicyId\") > 0 AND length(\"PolicyVersion\") > 0 AND length(\"Category\") > 0");
        });

        builder.HasKey(record => record.RecordId);
        builder.Property(record => record.RecordId).ValueGeneratedNever();
        builder.Property(record => record.SubjectIdentityId).IsRequired();
        builder.Property(record => record.Category).HasMaxLength(64).IsRequired();
        builder.Property(record => record.PolicyId).HasMaxLength(128).IsRequired();
        builder.Property(record => record.PolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ExecutedAt).IsRequired();
        builder.Property(record => record.AffectedRowCount).IsRequired();

        builder.HasIndex(record => new { record.SubjectIdentityId, record.Category });

        // Restrict, not cascade. The evidence must outlive every attempt to tidy it away, including deleting the
        // identity it names (IA-REQ-056).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(record => record.SubjectIdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}
