using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class AccountReactivationRequestConfiguration : IEntityTypeConfiguration<AccountReactivationRequest>
{
    public void Configure(EntityTypeBuilder<AccountReactivationRequest> builder)
    {
        builder.ToTable("AccountReactivationRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_AccountReactivationRequests_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // A settled ticket and the moment it settled are one fact, and a token hash that is not a versioned
            // digest is not a hash of anything this system produced.
            table.HasCheckConstraint(
                "CK_AccountReactivationRequests_Lifecycle",
                "\"Version\" > 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"Status\" = 'Pending') = (\"SettledAt\" IS NULL) AND \"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$'");
        });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.IdentityId).IsRequired();
        builder.Property(request => request.TokenHash)
            .HasConversion(hash => hash.Value, value => VersionedTokenHash.FromPersistedValue(value))
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(request => request.IssuedAt).IsRequired();
        builder.Property(request => request.ExpiresAt).IsRequired();
        builder.Property(request => request.SettledAt);
        builder.Property(request => request.Version).IsConcurrencyToken().IsRequired();

        // One live ticket per identity. Reissuing supersedes rather than stacks, so a ticket somebody asked for
        // twice is never two working tickets.
        builder.HasIndex(request => request.IdentityId).IsUnique().HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(request => request.TokenHash).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(request => request.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
