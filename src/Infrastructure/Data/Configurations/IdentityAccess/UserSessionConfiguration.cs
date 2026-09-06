using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", table => table.HasCheckConstraint("CK_UserSessions_Lifecycle", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Version\" > 0 AND \"CreatedAt\" <= \"LastSeenAt\" AND \"LastSeenAt\" <= \"IdleExpiresAt\" AND \"IdleExpiresAt\" <= \"AbsoluteExpiresAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")"));
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasConversion(id => id.Value, value => UserSessionId.From(value)).ValueGeneratedNever();
        builder.Property(session => session.IdentityId).IsRequired();
        builder.Property(session => session.PublicRef)
            .HasConversion(reference => reference.Value, value => SessionReference.FromPersistedValue(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(session => session.DeviceLabel).HasMaxLength(32).IsRequired();
        builder.Property(session => session.ActiveTenantId).HasConversion<Guid?>(id => id.HasValue ? id.Value.Value : null, value => value.HasValue ? TenantId.From(value.Value) : null).IsRequired(false);
        builder.Property(session => session.CreatedAt).IsRequired();
        builder.Property(session => session.LastSeenAt).IsRequired();
        builder.Property(session => session.IdleExpiresAt).IsRequired();
        builder.Property(session => session.AbsoluteExpiresAt).IsRequired();
        builder.Property(session => session.Version).IsConcurrencyToken().IsRequired();
        builder.HasIndex(session => session.PublicRef).IsUnique();
        builder.HasIndex(session => session.IdentityId);
        builder.HasIndex(session => new { session.IdentityId, session.RevokedAt, session.AbsoluteExpiresAt });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.IdentityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(session => session.ActiveTenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
