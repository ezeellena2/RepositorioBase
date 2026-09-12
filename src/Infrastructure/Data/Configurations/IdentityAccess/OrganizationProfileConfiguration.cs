using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class OrganizationProfileConfiguration : IEntityTypeConfiguration<OrganizationProfile>
{
    public void Configure(EntityTypeBuilder<OrganizationProfile> builder)
    {
        builder.ToTable("OrganizationProfiles", table => table.HasCheckConstraint("CK_OrganizationProfiles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(profile => profile.TenantId);
        builder.Property(profile => profile.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).ValueGeneratedNever();
        builder.Property(profile => profile.LegalName).HasMaxLength(256).IsRequired();
        builder.Property(profile => profile.Cuit).HasConversion(cuit => cuit.Value, value => NormalizedCuit.FromStored(value)).HasMaxLength(11).IsRequired();
        builder.HasIndex(profile => profile.Cuit).IsUnique();
        builder.HasOne<Tenant>().WithOne().HasForeignKey<OrganizationProfile>(profile => profile.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
