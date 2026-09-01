using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("TenantMemberships", table => table.HasCheckConstraint("CK_TenantMemberships_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).HasConversion(id => id.Value, value => MembershipId.From(value)).ValueGeneratedNever();
        builder.Property(membership => membership.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(membership => membership.IdentityId).IsRequired();
        builder.Property(membership => membership.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasIndex(membership => new { membership.TenantId, membership.IdentityId }).IsUnique();
        builder.HasAlternateKey(membership => new { membership.TenantId, membership.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(membership => membership.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(membership => membership.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
