using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class PlatformAdminInvitationConfiguration : IEntityTypeConfiguration<PlatformAdminInvitation>
{
    /// <summary>
    /// The aggregate's rules restated where PostgreSQL enforces them too. The recipient and token clauses are the
    /// same ones an organization invitation carries, for the same reasons: one canonical spelling per address, and
    /// nothing but a real versioned hash in the token column.
    /// <para>
    /// The clauses that are this aggregate's own are the delivery pair and the acceptance binding. Delivery is
    /// settled or it is not, and the timestamp must say which — recovery reads that state to decide whether it may
    /// rotate a token, so a row claiming a permanent failure with no settlement is a row that could invalidate a
    /// token still in flight. And an accepted invitation must name the identity it was accepted by: a Platform
    /// membership is activated from that binding, so an acceptance with nobody bound would be an activation with
    /// no one to activate.
    /// </para>
    /// </summary>
    private const string LifecycleConstraint =
        "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$' AND " +
        "\"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND " +
        "\"NormalizedEmail\" = lower(\"NormalizedEmail\") AND " +
        "\"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND " +
        "strpos(\"NormalizedEmail\", '@') > 0 AND " +
        "\"ExpiresAt\" > \"CreatedAt\" AND " +
        "((\"BoundIdentityId\" IS NULL) = (\"BoundAt\" IS NULL)) AND " +
        "(\"BoundAt\" IS NULL OR \"BoundAt\" >= \"CreatedAt\") AND " +
        "((\"Delivery\" = 'Pending') = (\"DeliverySettledAt\" IS NULL)) AND " +
        "(\"DeliverySettledAt\" IS NULL OR \"DeliverySettledAt\" >= \"CreatedAt\") AND (" +
        "(\"Status\" = 'Pending' AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR " +
        "(\"Status\" = 'Accepted' AND \"BoundIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR " +
        "(\"Status\" = 'Cancelled' AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))";

    public void Configure(EntityTypeBuilder<PlatformAdminInvitation> builder)
    {
        builder.ToTable("PlatformAdminInvitations", table =>
        {
            table.HasCheckConstraint(
                "CK_PlatformAdminInvitations_Ids_NotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"BoundIdentityId\" IS NULL OR \"BoundIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("CK_PlatformAdminInvitations_Lifecycle", LifecycleConstraint);
        });
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).HasConversion(id => id.Value, value => PlatformAdminInvitationId.From(value)).ValueGeneratedNever();
        builder.Property(invitation => invitation.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(invitation => invitation.TokenHash)
            .HasConversion(hash => hash.Value, value => VersionedTokenHash.FromPersistedValue(value))
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(invitation => invitation.Delivery).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(invitation => invitation.IsOwner).IsRequired();
        builder.Property(invitation => invitation.CreatedAt).IsRequired();
        builder.Property(invitation => invitation.ExpiresAt).IsRequired();
        builder.Property(invitation => invitation.BoundIdentityId);
        builder.Property(invitation => invitation.BoundAt);
        builder.Property(invitation => invitation.AcceptedAt);
        builder.Property(invitation => invitation.CancelledAt);
        builder.Property(invitation => invitation.DeliveryMessageId);
        builder.Property(invitation => invitation.DeliverySettledAt);
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");

        // A token resolves to at most one invitation in any state, so a rotated or settled one can never be
        // replayed against a different row.
        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();

        // At most one live offer per recipient. Platform is the singleton tenant, so the recipient alone is the
        // key — there is no second Platform for the same address to be invited to.
        builder.HasIndex(invitation => invitation.NormalizedEmail).IsUnique().HasFilter("\"Status\" = 'Pending'");

        // And at most one pending owner, which is what bootstrap recovery has to preserve (IA-REQ-040). Holding it
        // here means a concurrent recovery cannot leave two live owner offers behind.
        builder.HasIndex(invitation => invitation.IsOwner).IsUnique().HasFilter("\"Status\" = 'Pending' AND \"IsOwner\"");

        builder.HasIndex(invitation => invitation.ExpiresAt);

        // Neither the tenant nor the bound identity may erase Platform invitation history (IA-REQ-036).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(invitation => invitation.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(invitation => invitation.BoundIdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
