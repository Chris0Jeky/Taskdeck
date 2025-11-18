using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Labels");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .ValueGeneratedNever();

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(l => l.ColorHex)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .IsRequired();

        builder.HasMany(l => l.CardLabels)
            .WithOne(cl => cl.Label)
            .HasForeignKey(cl => cl.LabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
