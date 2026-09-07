using CleanArchitecture.Domain.IdentityAccess.Retention;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class RetentionLegalHoldConfiguration : IEntityTypeConfiguration<RetentionLegalHold>
{
    public void Configure(EntityTypeBuilder<RetentionLegalHold> builder)
    {
        builder.ToTable("RetentionLegalHolds", table =>
        {
            table.HasCheckConstraint(
                "CK_RetentionLegalHolds_Ids_NotEmpty",
                "\"HoldId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PlacedByMembershipId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // The shape restated where PostgreSQL enforces it too. A retention record is read by people who are
            // not the subject, so what can be written into one is not left to the code that happens to write it.
            table.HasCheckConstraint(
                "CK_RetentionLegalHolds_Shape",
                "\"Version\" > 0 AND (\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"PlacedAt\") AND " +
                "\"ReasonCode\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND \"Reference\" ~ '^[A-Za-z0-9._:-]{1,64}$'");
        });

        builder.HasKey(hold => hold.HoldId);
        builder.Property(hold => hold.HoldId).ValueGeneratedNever();
        builder.Property(hold => hold.SubjectIdentityId).IsRequired();
        builder.Property(hold => hold.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(hold => hold.Reference).HasMaxLength(64).IsRequired();
        builder.Property(hold => hold.PlacedAt).IsRequired();
        builder.Property(hold => hold.PlacedByMembershipId).IsRequired();
        builder.Property(hold => hold.ReleasedAt);
        builder.Property(hold => hold.Version).IsConcurrencyToken().IsRequired();

        // One standing hold per subject and reason. Two operators placing the same one concurrently meet this
        // rather than leaving two records that both have to be released.
        builder.HasIndex(hold => new { hold.SubjectIdentityId, hold.ReasonCode })
            .IsUnique()
            .HasFilter("\"ReleasedAt\" IS NULL");

        // Released holds are kept: the record of what was held and when is the evidence, and deleting it would
        // erase exactly the thing a hold exists to protect.
        builder.HasIndex(hold => hold.SubjectIdentityId).HasFilter("\"ReleasedAt\" IS NULL");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(hold => hold.SubjectIdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}
