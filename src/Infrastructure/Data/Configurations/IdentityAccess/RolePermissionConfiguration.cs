using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", table => table.HasCheckConstraint("CK_RolePermissions_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(assignment => new { assignment.TenantId, assignment.RoleId, assignment.PermissionCode });
        builder.Property(assignment => assignment.TenantId).HasConversion(id => id.Value, value => TenantId.From(value));
        builder.Property(assignment => assignment.RoleId).HasConversion(id => id.Value, value => RoleId.From(value));
        builder.Property(assignment => assignment.PermissionCode).HasMaxLength(128);
        builder.HasIndex(assignment => assignment.PermissionCode);
        builder.HasOne<Role>().WithMany().HasForeignKey(assignment => new { assignment.TenantId, assignment.RoleId }).HasPrincipalKey(role => new { role.TenantId, role.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Permission>().WithMany().HasForeignKey(assignment => assignment.PermissionCode).OnDelete(DeleteBehavior.Restrict);
    }
}
