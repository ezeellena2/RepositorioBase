using CleanArchitecture.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Data.Configurations;

public class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        builder.ToTable("TodoItems", table => table.HasCheckConstraint("CK_TodoItems_AuditActors_NotEmpty", "(\"CreatedBy\" IS NULL OR \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"LastModifiedBy\" IS NULL OR \"LastModifiedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.Property(t => t.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property<uint>("Version")
            .IsRowVersion()
            .HasColumnName("xmin");
    }
}
