using CleanArchitecture.Domain.IdentityAccess.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

/// <summary>
/// The intent table deliberately has no unique index on the address or the CUIT (IA-REQ-048).
/// <para>
/// Such an index would be exactly the exclusive reservation this table exists not to make: it would let one
/// unproven request refuse another, and the refusal would say whether the address it named already had an
/// account. The only exclusive claims stay where they were — the unique CUIT index on `OrganizationProfile` and
/// the unique normalized email on the identity — and both are reached only after the address is proved.
/// </para>
/// </summary>
public sealed class PendingRegistrationIntentConfiguration : IEntityTypeConfiguration<PendingRegistrationIntent>
{
    public void Configure(EntityTypeBuilder<PendingRegistrationIntent> builder)
    {
        builder.ToTable("pending_registration_intents", table =>
        {
            table.HasCheckConstraint(
                "CK_pending_registration_intents_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubmissionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // An outcome and the moment it was reached are one fact; a row carrying one without the other would be
            // a settlement nobody can date or a date for a settlement that never happened.
            table.HasCheckConstraint(
                "CK_pending_registration_intents_Settlement",
                "(\"Outcome\" IS NULL) = (\"CompletedAt\" IS NULL) AND \"ExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint(
                "CK_pending_registration_intents_Language",
                "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");
        });

        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.SubmissionId).IsRequired();
        builder.Property(intent => intent.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(intent => intent.Language).HasMaxLength(16);
        builder.Property(intent => intent.LegalName).HasMaxLength(256).IsRequired();
        builder.Property(intent => intent.Cuit)
            .HasConversion(cuit => cuit.Value, value => NormalizedCuit.From(value))
            .HasMaxLength(11)
            .IsRequired();
        builder.Property(intent => intent.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(intent => intent.CreatedAt).IsRequired();
        builder.Property(intent => intent.ExpiresAt).IsRequired();
        builder.Property(intent => intent.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(intent => intent.CompletedAt);

        // One intent per submission: the submission's canonical key is what makes an equivalent replay return the
        // recorded answer instead of writing a second intent.
        builder.HasIndex(intent => intent.SubmissionId).IsUnique();

        builder.HasOne<RegistrationSubmission>()
            .WithMany()
            .HasForeignKey(intent => intent.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
