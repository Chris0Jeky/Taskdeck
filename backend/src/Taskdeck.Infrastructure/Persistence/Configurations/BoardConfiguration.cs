using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Boards");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .ValueGeneratedNever();

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.Description)
            .HasMaxLength(500);

        builder.Property(b => b.IsArchived)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired();

        builder.HasMany(b => b.Columns)
            .WithOne(c => c.Board)
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Cards)
            .WithOne(c => c.Board)
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Labels)
            .WithOne(l => l.Board)
            .HasForeignKey(l => l.BoardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
