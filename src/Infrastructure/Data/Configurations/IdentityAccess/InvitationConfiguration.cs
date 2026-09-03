using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    /// <summary>
    /// Every rule the aggregate enforces, restated where the database can enforce it too: a recipient that is
    /// present, trimmed and carries no uppercase; a token stored only as a versioned hash; a window that outlives
    /// its own creation; and terminal evidence that matches the status it belongs to.
    /// <para>
    /// The casing clause deliberately does NOT use <c>lower()</c>. PostgreSQL's <c>lower()</c> is locale-aware
    /// while the aggregate uses .NET's invariant simple case mapping, and the two disagree on real characters —
    /// U+0130 (Turkish dotted capital I) is valid in an SMTPUTF8 local part, survives <c>ToLowerInvariant</c>
    /// unchanged, and would then be rejected by <c>lower()</c>, turning a legitimate invitation into an
    /// unhandled constraint violation. Rejecting ASCII uppercase by explicit enumeration is locale-proof and can
    /// never reject a value the aggregate produced.
    /// </para>
    /// <para>
    /// For the same reason <c>AcceptedAt &lt;= ExpiresAt</c> is not strict, even though the aggregate requires
    /// <c>now &lt; ExpiresAt</c>. Npgsql truncates a <c>DateTimeOffset</c> to microseconds, so a window whose
    /// expiry carries sub-microsecond ticks can turn a legal acceptance into one that rounds onto the boundary,
    /// and a strict comparison would reject it. Admitting the exact boundary costs nothing the aggregate can
    /// reach; a genuine overrun is still rejected.
    /// </para>
    /// </summary>
    private const string LifecycleConstraint =
        "\"TokenHash\" ~ '^v[0123456789]+:' AND " +
        "\"NormalizedEmail\" !~ '[ABCDEFGHIJKLMNOPQRSTUVWXYZ]' AND " +
        "\"NormalizedEmail\" = btrim(\"NormalizedEmail\") AND " +
        "strpos(\"NormalizedEmail\", '@') > 0 AND " +
        "\"ExpiresAt\" > \"CreatedAt\" AND (" +
        "(\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR " +
        "(\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR " +
        "(\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))";

    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations", table =>
        {
            table.HasCheckConstraint("CK_Invitations_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"AcceptedByIdentityId\" IS NULL OR \"AcceptedByIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("CK_Invitations_Lifecycle", LifecycleConstraint);
        });
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).HasConversion(id => id.Value, value => InvitationId.From(value)).ValueGeneratedNever();
        builder.Property(invitation => invitation.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(256).IsRequired();
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(invitation => invitation.CreatedAt).IsRequired();
        builder.Property(invitation => invitation.ExpiresAt).IsRequired();
        builder.Property(invitation => invitation.AcceptedAt);
        builder.Property(invitation => invitation.AcceptedByIdentityId);
        builder.Property(invitation => invitation.CancelledAt);
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasAlternateKey(invitation => new { invitation.TenantId, invitation.Id });

        // A token resolves to at most one invitation in any state, so a rotated or settled one can never be
        // replayed against a different row.
        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();

        // At most one live invitation per recipient (IA-REQ-017). The filter is on the stored status only, which
        // is why expiry is derived rather than stored: reissue revives the same row instead of leaving a settled
        // one behind to block it, and an accepted or cancelled invitation never blocks a fresh invitation.
        builder.HasIndex(invitation => new { invitation.TenantId, invitation.NormalizedEmail })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        // The read path a recipient lookup takes; the filtered index above cannot serve a parameterised status.
        builder.HasIndex(invitation => new { invitation.TenantId, invitation.NormalizedEmail, invitation.Status });
        builder.HasIndex(invitation => invitation.ExpiresAt);

        builder.HasMany(invitation => invitation.Roles)
            .WithOne()
            .HasForeignKey(offered => new { offered.TenantId, offered.InvitationId })
            .HasPrincipalKey(invitation => new { invitation.TenantId, invitation.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(invitation => invitation.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Neither a tenant nor an accepting identity may erase invitation history (IA-REQ-036).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(invitation => invitation.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(invitation => invitation.AcceptedByIdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
