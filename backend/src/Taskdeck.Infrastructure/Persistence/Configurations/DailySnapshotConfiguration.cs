using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class DailySnapshotConfiguration : IEntityTypeConfiguration<DailySnapshot>
{
    public void Configure(EntityTypeBuilder<DailySnapshot> builder)
    {
        builder.ToTable("DailySnapshots");

        builder.HasKey(ds => ds.Id);

        builder.Property(ds => ds.Id)
            .ValueGeneratedNever();

        builder.Property(ds => ds.UserId)
            .IsRequired();

        builder.Property(ds => ds.Date)
            .IsRequired();

        builder.Property(ds => ds.SealedAt);

        builder.Property(ds => ds.CreatedAt)
            .IsRequired();

        builder.Property(ds => ds.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        // Unique constraint: one snapshot per user per day
        builder.HasIndex(ds => new { ds.UserId, ds.Date })
            .IsUnique();

        builder.HasIndex(ds => ds.UserId);
    }
}
