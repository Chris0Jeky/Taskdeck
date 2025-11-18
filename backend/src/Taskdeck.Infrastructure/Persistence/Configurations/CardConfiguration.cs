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
    }
}
