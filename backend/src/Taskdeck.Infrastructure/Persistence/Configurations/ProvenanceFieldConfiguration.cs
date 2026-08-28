using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProvenanceFieldConfiguration : IEntityTypeConfiguration<ProvenanceField>
{
    public void Configure(EntityTypeBuilder<ProvenanceField> builder)
    {
        builder.ToTable("ProvenanceFields");

        builder.HasKey(pf => pf.Id);

        builder.Property(pf => pf.Id)
            .ValueGeneratedNever();

        builder.Property(pf => pf.FieldName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pf => pf.Kind)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(pf => pf.Confidence)
            .IsRequired(false);

        builder.Property(pf => pf.ConfidenceSource)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(pf => pf.ExtractiveQuote)
            .HasMaxLength(2000);

        builder.Property(pf => pf.ProposalProvenanceId)
            .IsRequired();

        builder.Property(pf => pf.CreatedAt)
            .IsRequired();

        builder.Property(pf => pf.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(pf => pf.ProposalProvenanceId);

        builder.HasMany(pf => pf.EvidenceLinks)
            .WithOne()
            .HasForeignKey(el => el.ProvenanceFieldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
