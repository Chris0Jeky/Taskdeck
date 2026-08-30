using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class CaptureBackfillStateConfiguration : IEntityTypeConfiguration<CaptureBackfillState>
{
    public void Configure(EntityTypeBuilder<CaptureBackfillState> builder)
    {
        builder.ToTable("CaptureBackfillStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.Property(state => state.Key).HasMaxLength(CaptureBackfillState.MaxKeyLength).IsRequired();
        builder.Property(state => state.StartedAt).IsRequired();
        builder.Property(state => state.CompletedAt);
        builder.Property(state => state.MigratedCount).IsRequired();
        builder.Property(state => state.SkippedCount).IsRequired();
        builder.Property(state => state.LastSkipReason).HasMaxLength(CaptureBackfillState.MaxNoteLength);
        builder.Property(state => state.CreatedAt).IsRequired();
        builder.Property(state => state.UpdatedAt).IsRequired();
        builder.Ignore(state => state.IsComplete);

        builder.HasIndex(state => state.Key).IsUnique();
    }
}
