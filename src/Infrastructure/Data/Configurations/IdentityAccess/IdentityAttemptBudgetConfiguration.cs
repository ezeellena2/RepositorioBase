using CleanArchitecture.Infrastructure.IdentityAccess.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;

public sealed class IdentityAttemptBudgetConfiguration : IEntityTypeConfiguration<IdentityAttemptBudget>
{
    public void Configure(EntityTypeBuilder<IdentityAttemptBudget> builder)
    {
        builder.ToTable("IdentityAttemptBudgets", table => table.HasCheckConstraint(
            "CK_IdentityAttemptBudgets_Window",
            "\"Count\" >= 0 AND \"ExpiresAt\" > \"WindowStart\" AND \"Scope\" <> '' AND \"KeyHash\" <> ''"));
        builder.HasKey(budget => new { budget.Scope, budget.KeyHash, budget.WindowStart });
        builder.Property(budget => budget.Scope).HasMaxLength(64).IsRequired();
        builder.Property(budget => budget.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(budget => budget.WindowStart).IsRequired();
        builder.Property(budget => budget.Count).IsRequired();
        builder.Property(budget => budget.ExpiresAt).IsRequired();

        // The cleanup path Task 27 owns reads this; the budget path itself never scans.
        builder.HasIndex(budget => budget.ExpiresAt);
    }
}
