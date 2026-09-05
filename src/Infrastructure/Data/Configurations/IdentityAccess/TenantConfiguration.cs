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

        // Suspension evidence. The reason is a closed set stored by name, so a read-only Platform projection
        // cannot become a place free text — very plausibly personal data — is written into (IA-REQ-043/044).
        builder.Property(tenant => tenant.SuspensionReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(tenant => tenant.SuspendedAt);

        // Operational timestamps, kept out of the aggregate because no domain rule depends on them. They are
        // stamped by an interceptor and read only by the Platform projection.
        builder.Property<DateTimeOffset>("CreatedAt").IsRequired();
        builder.Property<DateTimeOffset>("UpdatedAt").IsRequired();

        builder.ToTable("Tenants", table => table.HasCheckConstraint(
            "CK_Tenants_Suspension",
            "((\"Status\" = 'Suspended') OR (\"SuspensionReason\" IS NULL AND \"SuspendedAt\" IS NULL)) AND " +
            "((\"SuspensionReason\" IS NULL) = (\"SuspendedAt\" IS NULL)) AND \"UpdatedAt\" >= \"CreatedAt\""));
    }
}
