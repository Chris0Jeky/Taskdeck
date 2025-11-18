using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CardLabelConfiguration : IEntityTypeConfiguration<CardLabel>
{
    public void Configure(EntityTypeBuilder<CardLabel> builder)
    {
        builder.ToTable("CardLabels");

        builder.HasKey(cl => new { cl.CardId, cl.LabelId });

        builder.HasOne(cl => cl.Card)
            .WithMany(c => c.CardLabels)
            .HasForeignKey(cl => cl.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cl => cl.Label)
            .WithMany(l => l.CardLabels)
            .HasForeignKey(cl => cl.LabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
