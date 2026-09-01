using CleanArchitecture.Domain.IdentityAccess.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class RegistrationSubmissionConfiguration : IEntityTypeConfiguration<RegistrationSubmission>
{
    public void Configure(EntityTypeBuilder<RegistrationSubmission> builder)
    {
        builder.ToTable("registration_submissions", table => table.HasCheckConstraint("CK_registration_submissions_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CanonicalKey).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => x.CanonicalKey).IsUnique();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32);
    }
}
