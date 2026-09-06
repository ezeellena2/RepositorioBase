using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class PersonProfileConfiguration : IEntityTypeConfiguration<PersonProfile>
{
    public void Configure(EntityTypeBuilder<PersonProfile> builder)
    {
        builder.ToTable("PersonProfiles", table => table.HasCheckConstraint(
            "CK_PersonProfiles_Ids_NotEmpty",
            "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PersonalTenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(profile => profile.IdentityId);
        builder.Property(profile => profile.IdentityId).ValueGeneratedNever();
        builder.Property(profile => profile.PersonalTenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();
        builder.Property(profile => profile.FullName).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.DisplayName).HasMaxLength(60).IsRequired();
        builder.Property(profile => profile.Classification).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property<DateTimeOffset>("CreatedAt").IsRequired();
        builder.Property<DateTimeOffset>("UpdatedAt").IsRequired();
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(profile => profile.PersonalTenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(profile => profile.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PersonalTenantOwnershipConfiguration : IEntityTypeConfiguration<PersonalTenantOwnership>
{
    public void Configure(EntityTypeBuilder<PersonalTenantOwnership> builder)
    {
        builder.ToTable("PersonalTenantOwnerships", table => table.HasCheckConstraint(
            "CK_PersonalTenantOwnerships_Ids_NotEmpty",
            "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(ownership => ownership.IdentityId);
        builder.Property(ownership => ownership.IdentityId).ValueGeneratedNever();
        builder.Property(ownership => ownership.TenantId).HasConversion(id => id.Value, value => TenantId.From(value)).IsRequired();

        // Unique in both directions. The primary key already stops one identity owning two Personal contexts; this
        // index stops one Personal tenant being claimed by two identities, which a race could otherwise produce
        // between the tenant insert and the ownership insert.
        builder.HasIndex(ownership => ownership.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(ownership => ownership.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(ownership => ownership.IdentityId).OnDelete(DeleteBehavior.NoAction);
    }
}
