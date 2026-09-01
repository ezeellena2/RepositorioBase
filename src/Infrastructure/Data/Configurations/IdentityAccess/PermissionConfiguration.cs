using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        var converter = new ValueConverter<IReadOnlySet<TenantType>, string>(
            types => string.Join(',', types.OrderBy(type => type)),
            value => new HashSet<TenantType>(value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<TenantType>)));
        var comparer = new ValueComparer<IReadOnlySet<TenantType>>(
            (left, right) => left != null && right != null && left.SetEquals(right),
            types => types.OrderBy(type => type).Aggregate(0, (hash, type) => HashCode.Combine(hash, type)),
            types => new HashSet<TenantType>(types));

        builder.ToTable("Permissions", table => table.HasCheckConstraint("CK_Permissions_Code_NotEmpty", "length(\"Code\") > 0"));
        builder.HasKey(permission => permission.Code);
        builder.Property(permission => permission.Code).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(permission => permission.AllowedTenantTypes).HasConversion(converter).HasMaxLength(128).Metadata.SetValueComparer(comparer);
    }
}
