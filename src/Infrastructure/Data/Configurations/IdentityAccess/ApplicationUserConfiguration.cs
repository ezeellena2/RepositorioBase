using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("AspNetUsers", table =>
        {
            table.HasCheckConstraint("CK_AspNetUsers_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            // The state set is finite, and the database is where that stops being a convention. A row outside it
            // is not an account in an unusual state — it is an account whose state nothing can interpret, and the
            // one thing worse than refusing a sign-in is not knowing whether to (IA-REQ-054).
            table.HasCheckConstraint(
                "CK_AspNetUsers_Status",
                "\"Status\" IN ('PendingConfirmation', 'Active', 'SelfDeactivated', 'AdministrativelySuspended', 'Closed')");
        });
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");

        // The one column this project adds to ASP.NET Identity's user table (IA-REQ-054). Stored as its name
        // rather than as an ordinal, so reading the table says what a row means and reordering the enum cannot
        // silently reinterpret every account.
        // The default is the least any account may do, not the empty string a bare `ADD COLUMN` would leave. A
        // row inserted by something that has never heard of this column — a repair script, a fixture, a future
        // migration — is then an account nobody can sign into, which is the safe way to be wrong.
        builder.Property(user => user.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(IdentityAccountStatus.PendingConfirmation)
            .IsRequired();
    }
}

public sealed class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder) =>
        builder.ToTable("AspNetRoles", table => table.HasCheckConstraint("CK_AspNetRoles_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
}
