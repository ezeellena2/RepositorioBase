using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.ToTable("MembershipRoles", table => table.HasCheckConstraint("CK_MembershipRoles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(assignment => new { assignment.TenantId, assignment.MembershipId, assignment.RoleId });
        builder.Property(assignment => assignment.TenantId).HasConversion(id => id.Value, value => TenantId.From(value));
        builder.Property(assignment => assignment.MembershipId).HasConversion(id => id.Value, value => MembershipId.From(value));
        builder.Property(assignment => assignment.RoleId).HasConversion(id => id.Value, value => RoleId.From(value));
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.RoleId });
        builder.HasOne<TenantMembership>().WithMany().HasForeignKey(assignment => new { assignment.TenantId, assignment.MembershipId }).HasPrincipalKey(membership => new { membership.TenantId, membership.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(assignment => new { assignment.TenantId, assignment.RoleId }).HasPrincipalKey(role => new { role.TenantId, role.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
