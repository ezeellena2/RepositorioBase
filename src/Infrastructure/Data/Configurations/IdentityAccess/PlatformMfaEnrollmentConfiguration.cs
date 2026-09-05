using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class PlatformMfaEnrollmentConfiguration : IEntityTypeConfiguration<PlatformMfaEnrollment>
{
    /// <summary>
    /// The gate order, restated where PostgreSQL enforces it too. A row cannot claim to be verified without saying
    /// when, cannot be active without having been acknowledged, and cannot carry step-up evidence that names no
    /// session — a Platform mutation reads exactly those three things to decide whether it may proceed, so a row
    /// written around the aggregate must not be able to answer them dishonestly.
    /// </summary>
    private const string LifecycleConstraint =
        "length(\"EncryptedSecret\") > 0 AND " +
        "((\"LastVerifiedAt\" IS NULL) = (\"LastVerifiedSessionId\" IS NULL)) AND " +
        "(\"LastVerifiedAt\" IS NULL OR \"LastVerifiedAt\" >= \"CreatedAt\") AND (" +
        "(\"Status\" = 'Pending' AND \"VerifiedAt\" IS NULL AND \"RecoveryAcknowledgedAt\" IS NULL AND \"LastVerifiedAt\" IS NULL) OR " +
        "(\"Status\" = 'Verified' AND \"VerifiedAt\" IS NOT NULL AND \"VerifiedAt\" >= \"CreatedAt\" AND \"RecoveryAcknowledgedAt\" IS NULL) OR " +
        "(\"Status\" = 'Active' AND \"VerifiedAt\" IS NOT NULL AND \"RecoveryAcknowledgedAt\" IS NOT NULL AND \"RecoveryAcknowledgedAt\" >= \"VerifiedAt\"))";

    public void Configure(EntityTypeBuilder<PlatformMfaEnrollment> builder)
    {
        builder.ToTable("PlatformMfaEnrollments", table =>
        {
            table.HasCheckConstraint(
                "CK_PlatformMfaEnrollments_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"LastVerifiedSessionId\" IS NULL OR \"LastVerifiedSessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("CK_PlatformMfaEnrollments_Lifecycle", LifecycleConstraint);
        });
        builder.HasKey(enrollment => enrollment.Id);
        builder.Property(enrollment => enrollment.Id).HasConversion(id => id.Value, value => PlatformMfaEnrollmentId.From(value)).ValueGeneratedNever();
        builder.Property(enrollment => enrollment.IdentityId).IsRequired();
        builder.Property(enrollment => enrollment.EncryptedSecret).HasMaxLength(1024).IsRequired();
        builder.Property(enrollment => enrollment.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(enrollment => enrollment.CreatedAt).IsRequired();
        builder.Property(enrollment => enrollment.VerifiedAt);
        builder.Property(enrollment => enrollment.RecoveryAcknowledgedAt);
        builder.Property(enrollment => enrollment.LastVerifiedAt);
        builder.Property(enrollment => enrollment.LastVerifiedSessionId);
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");

        // One second factor per identity (SPEC section 5). A second row would be a second way in.
        builder.HasIndex(enrollment => enrollment.IdentityId).IsUnique();

        builder.HasMany(enrollment => enrollment.RecoveryCodes)
            .WithOne()
            .HasForeignKey(code => code.EnrollmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(enrollment => enrollment.RecoveryCodes).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(enrollment => enrollment.IdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlatformRecoveryCodeConfiguration : IEntityTypeConfiguration<PlatformRecoveryCode>
{
    public void Configure(EntityTypeBuilder<PlatformRecoveryCode> builder)
    {
        builder.ToTable("PlatformRecoveryCodes", table =>
        {
            table.HasCheckConstraint(
                "CK_PlatformRecoveryCodes_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"EnrollmentId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "CK_PlatformRecoveryCodes_Lifecycle",
                "length(\"CodeHash\") > 0 AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")");
        });
        builder.HasKey(code => code.Id);
        builder.Property(code => code.Id).ValueGeneratedNever();
        builder.Property(code => code.EnrollmentId).HasConversion(id => id.Value, value => PlatformMfaEnrollmentId.From(value)).IsRequired();
        builder.Property(code => code.CodeHash).HasMaxLength(256).IsRequired();
        builder.Property(code => code.CreatedAt).IsRequired();
        builder.Property(code => code.ConsumedAt);

        // A submitted code resolves to at most one row, so a hash collision cannot spend two codes at once.
        builder.HasIndex(code => new { code.EnrollmentId, code.CodeHash }).IsUnique();
    }
}
