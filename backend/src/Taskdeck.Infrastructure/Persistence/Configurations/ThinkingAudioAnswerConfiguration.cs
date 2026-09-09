using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class ThinkingAudioAnswerConfiguration : IEntityTypeConfiguration<ThinkingAudioAnswer>
{
    public void Configure(EntityTypeBuilder<ThinkingAudioAnswer> builder)
    {
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.QuestionHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.UserId, x.CardId, x.LayerId, x.QuestionHash }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.UploadId }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Board>().WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Capture>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceAsset>().WithMany().HasForeignKey(x => x.SourceAssetId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Representation>().WithMany().HasForeignKey(x => x.RepresentationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<WorkspaceMemory>().WithMany().HasForeignKey(x => x.ConfirmedMemoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
