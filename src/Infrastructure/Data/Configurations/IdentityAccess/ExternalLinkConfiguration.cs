using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

/// <summary>The two shadow columns a provider link carries beyond what ASP.NET Identity's own type has room for.</summary>
public static class ExternalLinkColumns
{
    public const string Handle = "Handle";

    public const string LinkedAt = "LinkedAt";
}

/// <summary>
/// Additive configuration on ASP.NET Identity's own <c>AspNetUserLogins</c>.
/// <para>
/// The link stays where the framework keeps it — a second registry would mean two rules about who owns a provider
/// account, and they could disagree. What is added is what this feature's contract shows and what its contract
/// promises: an opaque handle and a linking timestamp, both defaulted by the database so the framework's own
/// writes need no knowledge of them, and the uniqueness that makes "at most one link per provider per identity"
/// something PostgreSQL enforces rather than something a read-then-write could race.
/// </para>
/// </summary>
public sealed class ExternalLinkConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.Property<string>(ExternalLinkColumns.Handle)
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValueSql("translate(rtrim(encode(uuid_send(gen_random_uuid()), 'base64'), '='), '+/', '-_')");

        builder.Property<DateTimeOffset>(ExternalLinkColumns.LinkedAt)
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(login => new { login.LoginProvider, login.UserId })
            .IsUnique()
            .HasDatabaseName("UX_AspNetUserLogins_LoginProvider_UserId");
    }
}
