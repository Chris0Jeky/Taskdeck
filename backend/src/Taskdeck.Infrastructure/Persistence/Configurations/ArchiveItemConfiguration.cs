using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ArchiveItemConfiguration : IEntityTypeConfiguration<ArchiveItem>
{
    public void Configure(EntityTypeBuilder<ArchiveItem> builder)
    {
        builder.ToTable("ArchiveItems");

        builder.HasKey(ai => ai.Id);

        builder.Property(ai => ai.Id)
            .ValueGeneratedNever();

        builder.Property(ai => ai.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(ai => ai.EntityId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ai => ai.BoardId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ai => ai.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ai => ai.ArchivedByUserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ai => ai.ArchivedAt)
            .IsRequired();

        builder.Property(ai => ai.Reason)
            .HasMaxLength(500);

        builder.Property(ai => ai.SnapshotJson)
            .IsRequired();

        builder.Property(ai => ai.RestoreStatus)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ai => ai.RestoredAt);

        builder.Property(ai => ai.RestoredByUserId)
            .HasMaxLength(100);

        builder.Property(ai => ai.CreatedAt)
            .IsRequired();

        builder.Property(ai => ai.UpdatedAt)
            .IsRequired();

        builder.HasIndex(ai => ai.BoardId);
        builder.HasIndex(ai => new { ai.EntityType, ai.EntityId });
        builder.HasIndex(ai => ai.ArchivedByUserId);
        builder.HasIndex(ai => ai.RestoreStatus);
        builder.HasIndex(ai => ai.ArchivedAt);
    }
}
