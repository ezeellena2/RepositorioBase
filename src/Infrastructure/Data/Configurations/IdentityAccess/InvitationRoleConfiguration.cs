using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class InvitationRoleConfiguration : IEntityTypeConfiguration<InvitationRole>
{
    public void Configure(EntityTypeBuilder<InvitationRole> builder)
    {
        builder.ToTable("InvitationRoles", table => table.HasCheckConstraint("CK_InvitationRoles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(offered => new { offered.TenantId, offered.InvitationId, offered.RoleId });
        builder.Property(offered => offered.TenantId).HasConversion(id => id.Value, value => TenantId.From(value));
        builder.Property(offered => offered.InvitationId).HasConversion(id => id.Value, value => InvitationId.From(value));
        builder.Property(offered => offered.RoleId).HasConversion(id => id.Value, value => RoleId.From(value));
        builder.HasIndex(offered => new { offered.TenantId, offered.RoleId });

        // The foreign key to the invitation is declared from the aggregate side in InvitationConfiguration; both
        // it and this one repeat TenantId, so the single TenantId column has to satisfy the invitation and the
        // role at once and PostgreSQL rejects a cross-tenant pair even when the aggregate is bypassed
        // (IA-REQ-034). Neither side cascades: an offered role is history, not a detail to be swept away.
        builder.HasOne<Role>().WithMany()
            .HasForeignKey(offered => new { offered.TenantId, offered.RoleId })
            .HasPrincipalKey(role => new { role.TenantId, role.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
