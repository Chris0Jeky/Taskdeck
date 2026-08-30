using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class CaptureConfiguration : IEntityTypeConfiguration<Capture>
{
    public void Configure(EntityTypeBuilder<Capture> builder)
    {
        builder.ToTable("Captures");
        builder.HasKey(capture => capture.Id);
        builder.Property(capture => capture.Id).ValueGeneratedNever();
        builder.Property(capture => capture.UserId).IsRequired();
        builder.Property(capture => capture.CapturedAtServer).IsRequired();
        builder.Property(capture => capture.CapturedAtClient);
        builder.Property(capture => capture.PrimaryModality).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.OriginAdapter).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.Producer).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.Intent).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.Lifecycle).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.LegacySource).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.ContextBoardId);
        builder.Property(capture => capture.UserTitle).HasMaxLength(Capture.MaxUserTitleLength);
        builder.Property(capture => capture.UserNote).HasMaxLength(Capture.MaxUserNoteLength);
        builder.Property(capture => capture.LegacyRequestId);
        builder.Property(capture => capture.CreatedAt).IsRequired();
        builder.Property(capture => capture.UpdatedAt).IsRequired();

        builder.HasIndex(capture => new { capture.UserId, capture.CreatedAt });
        builder.HasIndex(capture => new { capture.UserId, capture.Lifecycle });
        builder.HasIndex(capture => capture.ContextBoardId);
        builder.HasIndex(capture => capture.LegacyRequestId).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(capture => capture.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(capture => capture.ContextBoardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
