using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class ExternalAuthorizationRequestConfiguration : IEntityTypeConfiguration<ExternalAuthorizationRequest>
{
    public void Configure(EntityTypeBuilder<ExternalAuthorizationRequest> builder)
    {
        builder.ToTable("ExternalAuthorizationRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_ExternalAuthorizationRequests_Id_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // A sign-in is the only purpose that may start without an identity, and a proof is the only one that
            // names an action. Both are the point of keeping the purposes apart, so both are enforced here too.
            table.HasCheckConstraint(
                "CK_ExternalAuthorizationRequests_Purpose",
                "\"Version\" > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND (\"Purpose\" = 'Login' OR \"IdentityId\" IS NOT NULL) AND (\"Purpose\" = 'Proof') = (\"Action\" IS NOT NULL)");
        });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Provider).HasMaxLength(64).IsRequired();
        builder.Property(request => request.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(request => request.IdentityId);
        builder.Property(request => request.SessionId)
            .HasConversion<Guid?>(id => id.HasValue ? id.Value.Value : null, value => value.HasValue ? UserSessionId.From(value.Value) : null)
            .IsRequired(false);
        builder.Property(request => request.Action).HasMaxLength(64);
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(request => request.CreatedAt).IsRequired();
        builder.Property(request => request.ExpiresAt).IsRequired();
        builder.Property(request => request.SettledAt);
        builder.Property(request => request.Subject).HasMaxLength(256);
        builder.Property(request => request.ProviderEmail).HasMaxLength(256);
        builder.Property(request => request.EmailVerified).IsRequired();
        builder.Property(request => request.Version).IsConcurrencyToken().IsRequired();

        builder.HasIndex(request => new { request.IdentityId, request.Status });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(request => request.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
