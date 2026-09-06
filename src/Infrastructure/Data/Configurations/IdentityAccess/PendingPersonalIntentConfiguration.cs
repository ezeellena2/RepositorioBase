using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

/// <summary>
/// Like the organization intent, this table has no unique index on the address and none on anything derived from the
/// document (IA-REQ-048). It holds sealed ciphertext and no fingerprint, so an unproved signup claims no documentary
/// identity and cannot refuse anybody; the unique index that does claim one lives on
/// <c>IdentityDocumentFingerprints</c>, and only a proved request reaches it.
/// </summary>
public sealed class PendingPersonalIntentConfiguration : IEntityTypeConfiguration<PendingPersonalIntent>
{
    public void Configure(EntityTypeBuilder<PendingPersonalIntent> builder)
    {
        builder.ToTable("pending_personal_intents", table =>
        {
            table.HasCheckConstraint(
                "CK_pending_personal_intents_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubmissionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            table.HasCheckConstraint(
                "CK_pending_personal_intents_Settlement",
                "(\"Outcome\" IS NULL) = (\"CompletedAt\" IS NULL) AND \"ExpiresAt\" > \"CreatedAt\"");
        });

        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.SubmissionId).IsRequired();
        builder.Property(intent => intent.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(intent => intent.FullName).HasMaxLength(200).IsRequired();
        builder.Property(intent => intent.DisplayName).HasMaxLength(60).IsRequired();
        builder.Property(intent => intent.DocumentCiphertext).HasColumnType("text").IsRequired();
        builder.Property(intent => intent.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(intent => intent.CreatedAt).IsRequired();
        builder.Property(intent => intent.ExpiresAt).IsRequired();
        builder.Property(intent => intent.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(intent => intent.CompletedAt);

        builder.HasIndex(intent => intent.SubmissionId).IsUnique();

        builder.HasOne<RegistrationSubmission>()
            .WithMany()
            .HasForeignKey(intent => intent.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
