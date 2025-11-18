using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ColumnConfiguration : IEntityTypeConfiguration<Column>
{
    public void Configure(EntityTypeBuilder<Column> builder)
    {
        builder.ToTable("Columns");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.Position)
            .IsRequired();

        builder.Property(c => c.WipLimit)
            .IsRequired(false);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasMany(c => c.Cards)
            .WithOne(card => card.Column)
            .HasForeignKey(card => card.ColumnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.BoardId, c.Position })
            .IsUnique();
    }
}
