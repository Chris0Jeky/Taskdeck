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
        builder.Property(capture => capture.ProducedByPrincipalId);
        builder.Property(capture => capture.ProducerKind).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.CapturedAtServer).IsRequired();
        builder.Property(capture => capture.CapturedAtClient);
        builder.Property(capture => capture.PrimaryModality).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.OriginAdapter).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.RequestedIntent).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.EffectiveIntent).HasConversion<int?>();
        builder.Property(capture => capture.IntentResolvedByRunId);
        builder.Property(capture => capture.Disposition).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.ProcessingSummary).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.ActionState).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.LegacySourceSnapshot).HasConversion<int>().IsRequired();
        builder.Property(capture => capture.ContextBoardId);
        builder.Property(capture => capture.UserTitle).HasMaxLength(Capture.MaxUserTitleLength);
        builder.Property(capture => capture.UserNote).HasMaxLength(Capture.MaxUserNoteLength);
        builder.Property(capture => capture.LegacyRequestId);
        builder.Property(capture => capture.CreatedAt).IsRequired();
        builder.Property(capture => capture.UpdatedAt).IsRequired();
        builder.Ignore(capture => capture.Timeline);

        builder.HasIndex(capture => new { capture.UserId, capture.CreatedAt });
        builder.HasIndex(capture => new { capture.UserId, capture.Disposition });
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

        // The aggregate owns its immutable inputs: they are loaded with it, saved with it and deleted
        // with it. The backing field keeps the domain's read-only view authoritative.
        builder.HasMany(capture => capture.SourceAssets)
            .WithOne()
            .HasForeignKey(asset => asset.CaptureId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(capture => capture.SourceAssets)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
