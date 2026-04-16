using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description)
            .HasMaxLength(4000);

        builder.Property(c => c.DueDate)
            .IsRequired(false);

        builder.Property(c => c.IsBlocked)
            .IsRequired();

        builder.Property(c => c.BlockReason)
            .HasMaxLength(500);

        builder.Property(c => c.Position)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasMany(c => c.CardLabels)
            .WithOne(cl => cl.Card)
            .HasForeignKey(cl => cl.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        // PERF-10: composite index for board+column lookups (the default board
        // load path filters by BoardId and then groups by ColumnId). SQLite can
        // also satisfy single-column filters on BoardId from the leftmost prefix
        // of this composite index, so the redundant single-column IX_Cards_BoardId
        // is dropped by the AddPerfIndexes migration. The FK on Cards.BoardId is
        // still enforced by SQLite via the FOREIGN KEY constraint itself (not the
        // index), and IX_Cards_ColumnId remains unchanged as an FK convention index.
        builder.HasIndex(c => new { c.BoardId, c.ColumnId })
            .HasDatabaseName("IX_Cards_BoardId_ColumnId");
    }
}
