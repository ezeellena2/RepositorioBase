using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", table => table.HasCheckConstraint("CK_Tenants_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).HasConversion(id => id.Value, value => TenantId.From(value)).ValueGeneratedNever();
        builder.Property(tenant => tenant.Slug).HasConversion(slug => slug.Value, value => TenantSlug.From(value)).HasMaxLength(128).IsRequired();
        builder.Property(tenant => tenant.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(tenant => tenant.AuthorizationVersion).IsRequired();
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasIndex(tenant => tenant.Slug).IsUnique();
    }
}
