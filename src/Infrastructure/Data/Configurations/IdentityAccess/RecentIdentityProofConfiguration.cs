using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class RecentIdentityProofConfiguration : IEntityTypeConfiguration<RecentIdentityProof>
{
    public void Configure(EntityTypeBuilder<RecentIdentityProof> builder)
    {
        builder.ToTable("RecentIdentityProofs", table =>
        {
            table.HasCheckConstraint(
                "CK_RecentIdentityProofs_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // A spent proof and the reason it was spent are one fact, and a proof that expires before it is issued
            // is a window nothing could ever fit through.
            table.HasCheckConstraint(
                "CK_RecentIdentityProofs_Lifecycle",
                "\"SecurityVersion\" >= 0 AND \"Version\" > 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"ConsumedAt\" IS NULL) = (\"ConsumedReason\" IS NULL)");
        });

        builder.HasKey(proof => proof.Id);
        builder.Property(proof => proof.IdentityId).IsRequired();
        builder.Property(proof => proof.SessionId).HasConversion(id => id.Value, value => UserSessionId.From(value)).IsRequired();
        builder.Property(proof => proof.Action).HasMaxLength(64).IsRequired();
        builder.Property(proof => proof.Method).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(proof => proof.SecurityVersion).IsRequired();
        builder.Property(proof => proof.IssuedAt).IsRequired();
        builder.Property(proof => proof.ExpiresAt).IsRequired();
        builder.Property(proof => proof.ConsumedAt);
        builder.Property(proof => proof.ConsumedReason).HasMaxLength(64);
        builder.Property(proof => proof.Version).IsConcurrencyToken().IsRequired();

        // At most one live proof per identity, session and action. Without it a caller could stockpile proofs and
        // spend them long after the password that produced them was changed.
        builder.HasIndex(proof => new { proof.IdentityId, proof.SessionId, proof.Action })
            .IsUnique()
            .HasFilter("\"ConsumedAt\" IS NULL");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(proof => proof.IdentityId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserSession>().WithMany().HasForeignKey(proof => proof.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IdentitySecurityStateConfiguration : IEntityTypeConfiguration<IdentitySecurityState>
{
    public void Configure(EntityTypeBuilder<IdentitySecurityState> builder)
    {
        builder.ToTable("IdentitySecurityStates", table => table.HasCheckConstraint(
            "CK_IdentitySecurityStates_State",
            "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SecurityVersion\" >= 0 AND \"Version\" > 0"));
        builder.HasKey(state => state.IdentityId);
        builder.Property(state => state.IdentityId).ValueGeneratedNever();
        builder.Property(state => state.SecurityVersion).IsRequired();
        builder.Property(state => state.UpdatedAt).IsRequired();
        builder.Property(state => state.Version).IsConcurrencyToken().IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(state => state.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
