using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class TranscriptConfiguration : IEntityTypeConfiguration<Transcript>
{
    public void Configure(EntityTypeBuilder<Transcript> builder)
    {
        builder.ToTable("Transcripts");
        builder.HasKey(transcript => transcript.Id);
        builder.Property(transcript => transcript.Id).ValueGeneratedNever();
        builder.Property(transcript => transcript.UserId).IsRequired();
        builder.Property(transcript => transcript.BoardId);
        builder.Property(transcript => transcript.CaptureSource).HasConversion<int>().IsRequired();
        builder.Property(transcript => transcript.Text).HasMaxLength(Transcript.MaxTextLength).IsRequired();
        builder.Property(transcript => transcript.SegmentsJson)
            .HasMaxLength(Transcript.MaxSegmentsJsonLength)
            .IsRequired();
        builder.Property(transcript => transcript.CreatedFromCaptureId);
        builder.Property(transcript => transcript.SourceArtefactId);
        builder.Property(transcript => transcript.CreatedAt).IsRequired();
        builder.Property(transcript => transcript.UpdatedAt).IsRequired();
        builder.Ignore(transcript => transcript.Segments);

        builder.HasIndex(transcript => new { transcript.UserId, transcript.Id });
        builder.HasIndex(transcript => transcript.BoardId);
        builder.HasIndex(transcript => transcript.SourceArtefactId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transcript => transcript.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(transcript => transcript.BoardId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SourceArtefact>()
            .WithMany()
            .HasForeignKey(transcript => transcript.SourceArtefactId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
