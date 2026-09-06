using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class IdentityDocumentConfiguration : IEntityTypeConfiguration<IdentityDocument>
{
    /// <summary>
    /// Only Argentine national identity documents exist in this increment, and the two halves of a purge move
    /// together: a row either holds ciphertext and has not been purged, or holds none and has. Letting them diverge
    /// would leave a number that is unreadable but still occupies the uniqueness index, which is neither erased nor
    /// reclaimable (IA-REQ-056).
    /// </summary>
    private const string DocumentConstraint =
        "\"Country\" = 'AR' AND " +
        "\"DocumentType\" = 'DNI' AND " +
        "((\"PurgedAt\" IS NULL) = (\"Ciphertext\" <> '')) AND " +
        "(\"PurgedAt\" IS NULL OR \"PurgedAt\" >= \"RecordedAt\")";

    public void Configure(EntityTypeBuilder<IdentityDocument> builder)
    {
        builder.ToTable("IdentityDocuments", table =>
        {
            table.HasCheckConstraint(
                "CK_IdentityDocuments_Id_NotEmpty",
                "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_IdentityDocuments_Document", DocumentConstraint);
        });
        builder.HasKey(document => document.IdentityId);
        builder.Property(document => document.IdentityId).ValueGeneratedNever();
        builder.Property(document => document.Country).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.DocumentType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.Classification).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.Ciphertext).HasColumnType("text").IsRequired();
        builder.Property(document => document.RecordedAt).IsRequired();
        builder.Property(document => document.PurgedAt).IsRequired(false);
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");

        // Cascade, because a fingerprint has no meaning without the document it points at — the only other cascade
        // in the schema is the same shape (an enrollment and its recovery codes).
        builder.HasMany(document => document.Fingerprints)
            .WithOne()
            .HasForeignKey(fingerprint => fingerprint.IdentityId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(document => document.Fingerprints).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(document => document.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class IdentityDocumentFingerprintConfiguration : IEntityTypeConfiguration<IdentityDocumentFingerprint>
{
    public void Configure(EntityTypeBuilder<IdentityDocumentFingerprint> builder)
    {
        builder.ToTable("IdentityDocumentFingerprints", table => table.HasCheckConstraint(
            "CK_IdentityDocumentFingerprints_Fingerprint",
            "\"KeyVersion\" >= 1 AND \"Fingerprint\" ~ '^k[1-9][0-9]*:v1:[A-Za-z0-9+/]{43}=$'"));
        builder.HasKey(fingerprint => new { fingerprint.IdentityId, fingerprint.KeyVersion });
        builder.Property(fingerprint => fingerprint.IdentityId).ValueGeneratedNever();
        builder.Property(fingerprint => fingerprint.KeyVersion).ValueGeneratedNever();
        builder.Property(fingerprint => fingerprint.Fingerprint).HasMaxLength(64).IsRequired();

        // The rule the SPEC states as "unique among unpurged rows": a purge deletes these rows rather than blanking
        // them, so the index needs no filter to leave a purged number reclaimable.
        builder.HasIndex(fingerprint => fingerprint.Fingerprint)
            .IsUnique()
            .HasDatabaseName("UX_IdentityDocumentFingerprints_Fingerprint");
    }
}
