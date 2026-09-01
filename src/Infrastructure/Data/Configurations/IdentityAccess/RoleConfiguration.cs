using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", table => table.HasCheckConstraint("CK_Roles_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasConversion(id => id.Value, value => RoleId.From(value)).ValueGeneratedNever();
        builder.Property(role => role.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(128).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(128).IsRequired();
        builder.Property(role => role.IsSystem).IsRequired();
        builder.Property(role => role.IsRetired).IsRequired();
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasIndex(role => new { role.TenantId, role.NormalizedName }).IsUnique();
        builder.HasAlternateKey(role => new { role.TenantId, role.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(role => role.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
