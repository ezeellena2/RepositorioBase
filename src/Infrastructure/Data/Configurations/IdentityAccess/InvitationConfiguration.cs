using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    /// <summary>
    /// Every rule the aggregate enforces, restated where the database enforces it too: a recipient in exactly one
    /// canonical form, a token in exactly the format the hasher emits, a window that outlives its own creation,
    /// and terminal evidence that matches the status it belongs to.
    /// <para>
    /// Composition is verified with <c>normalize(x, NFC)</c>, which PostgreSQL evaluates exactly as .NET does, so
    /// two spellings of one address cannot each take a pending slot. The casing clause is <c>lower()</c> and that is safe only because the aggregate refuses to emit anything
    /// <c>lower()</c> would change: it rejects uppercase and titlecase categories outright rather than lowering
    /// them and hoping the two case mappings agree. They do not — U+0130 survives .NET's invariant mapping and is
    /// folded by PostgreSQL — so an earlier revision that lowered and then compared turned a legitimate
    /// invitation into an unhandled constraint violation. Refusing the character upstream lets both sides hold
    /// the same rule, which is what makes two spellings of one recipient unable to share the pending slot.
    /// </para>
    /// <para>
    /// The token clause pins the version, the digest length and the encoding's canonical form, so nothing but a
    /// real hash fits the column. The final data character is restricted to the sixteen whose low bits a 32-byte
    /// payload leaves unused, which is Base64 canonicality expressed as a pattern: without it several distinct
    /// strings decode to one digest, and the unique index would stop meaning one token per row. Adding a hash
    /// version means changing this constraint, the aggregate's constant and a migration together.
    /// </para>
    /// <para>
    /// The whitespace clause names U+00A0 separately because <c>[[:space:]]</c> is locale-classified and does not
    /// match a non-breaking space, which is the realistic way two spellings of one recipient would otherwise both
    /// reach the pending slot. The aggregate rejects the whole Unicode whitespace set; this is the backstop for
    /// the part of it that matters to uniqueness.
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
        "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$' AND " +
        "\"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND " +
        "\"NormalizedEmail\" = lower(\"NormalizedEmail\") AND " +
        "\"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND " +
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
            table.HasCheckConstraint("CK_Invitations_Language", "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");
        });
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).HasConversion(id => id.Value, value => InvitationId.From(value)).ValueGeneratedNever();
        builder.Property(invitation => invitation.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(invitation => invitation.Language).HasMaxLength(16);
        builder.Property(invitation => invitation.TokenHash)
            .HasConversion(hash => hash.Value, value => VersionedTokenHash.FromPersistedValue(value))
            .HasMaxLength(256)
            .IsRequired();
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
