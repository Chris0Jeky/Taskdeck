using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProvenanceEvidenceLinkConfiguration : IEntityTypeConfiguration<ProvenanceEvidenceLink>
{
    public void Configure(EntityTypeBuilder<ProvenanceEvidenceLink> builder)
    {
        builder.ToTable("ProvenanceEvidenceLinks");

        builder.HasKey(el => el.Id);

        builder.Property(el => el.Id)
            .ValueGeneratedNever();

        builder.Property(el => el.SourceType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(el => el.SourceId)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(el => el.TranscriptId);

        builder.Property(el => el.Label)
            .HasMaxLength(200);

        builder.Property(el => el.SpanStart);
        builder.Property(el => el.SpanEnd);

        builder.Property(el => el.ProvenanceFieldId)
            .IsRequired();

        builder.Property(el => el.CreatedAt)
            .IsRequired();

        builder.Property(el => el.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(el => el.ProvenanceFieldId);
        builder.HasIndex(el => el.TranscriptId);

        builder.HasOne<Transcript>()
            .WithMany()
            .HasForeignKey(el => el.TranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ProvenanceEvidenceLinks_TranscriptId",
            $"(\"SourceType\" = '{ProvenanceEvidenceLink.TranscriptSourceType}' AND \"TranscriptId\" IS NOT NULL) OR (\"SourceType\" <> '{ProvenanceEvidenceLink.TranscriptSourceType}' AND \"TranscriptId\" IS NULL)"));
    }
}
