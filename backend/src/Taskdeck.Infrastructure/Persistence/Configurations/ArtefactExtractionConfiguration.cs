using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class ArtefactExtractionConfiguration : IEntityTypeConfiguration<ArtefactExtraction>
{
    public void Configure(EntityTypeBuilder<ArtefactExtraction> builder)
    {
        builder.ToTable("ArtefactExtractions");
        builder.HasKey(extraction => extraction.Id);
        builder.Property(extraction => extraction.Id).ValueGeneratedNever();
        builder.Property(extraction => extraction.SourceArtefactId).IsRequired();
        builder.Property(extraction => extraction.ExtractorName)
            .HasMaxLength(ArtefactExtraction.MaxExtractorNameLength)
            .IsRequired();
        builder.Property(extraction => extraction.ExtractorVersion)
            .HasMaxLength(ArtefactExtraction.MaxExtractorVersionLength)
            .IsRequired();
        builder.Property(extraction => extraction.WarningsJson)
            .HasMaxLength(ArtefactExtraction.MaxWarningsJsonLength)
            .IsRequired();
        builder.Property(extraction => extraction.ExtractedText)
            .HasMaxLength(ArtefactExtraction.MaxExtractedTextLength)
            .IsRequired();
        builder.Property(extraction => extraction.TextLength).IsRequired();
        builder.Property(extraction => extraction.CreatedAt).IsRequired();
        builder.Property(extraction => extraction.UpdatedAt).IsRequired();

        builder.HasIndex(extraction => new { extraction.SourceArtefactId, extraction.CreatedAt });

        builder.HasOne<SourceArtefact>()
            .WithMany()
            .HasForeignKey(extraction => extraction.SourceArtefactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
