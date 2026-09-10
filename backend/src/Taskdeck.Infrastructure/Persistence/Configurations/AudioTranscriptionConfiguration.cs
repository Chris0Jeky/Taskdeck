using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class AudioTranscriptionAttemptConfiguration : IEntityTypeConfiguration<AudioTranscriptionAttempt>
{
    public void Configure(EntityTypeBuilder<AudioTranscriptionAttempt> builder)
    {
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.RequestHash).HasMaxLength(64); builder.Property(x => x.ConfigurationHash).HasMaxLength(64);
        builder.Property(x => x.Provider).HasMaxLength(100); builder.Property(x => x.Model).HasMaxLength(100);
        builder.Property(x => x.FailureCode).HasMaxLength(40);
        builder.HasIndex(x => new { x.UserId, x.RequestId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.AudioAnswerId });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ThinkingAudioAnswer>().WithMany().HasForeignKey(x => x.AudioAnswerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Capture>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<SourceAsset>().WithMany().HasForeignKey(x => x.SourceAssetId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Representation>().WithMany().HasForeignKey(x => x.RepresentationId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AudioTranscriptionBudgetConfiguration : IEntityTypeConfiguration<AudioTranscriptionBudget>
{
    public void Configure(EntityTypeBuilder<AudioTranscriptionBudget> builder)
    {
        builder.HasKey(x => x.UserId); builder.Property(x => x.UserId).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
