using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProposalOutcomeConfiguration : IEntityTypeConfiguration<ProposalOutcome>
{
    public void Configure(EntityTypeBuilder<ProposalOutcome> builder)
    {
        builder.ToTable("ProposalOutcomes");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.ProposalId)
            .IsRequired();

        builder.Property(o => o.DecidedByUserId)
            .IsRequired();

        builder.Property(o => o.Decision)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(o => o.DecisionLatencySeconds)
            .IsRequired();

        builder.Property(o => o.FieldCount)
            .IsRequired();

        builder.Property(o => o.EditedFieldCount)
            .IsRequired();

        builder.Property(o => o.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.RiskLevel)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.ModelId)
            .HasMaxLength(100);

        builder.Property(o => o.AverageFieldConfidence);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        // Indexes for common query patterns
        builder.HasIndex(o => o.ProposalId)
            .IsUnique(); // One outcome per proposal

        builder.HasIndex(o => o.DecidedByUserId);
        builder.HasIndex(o => o.Decision);
        builder.HasIndex(o => o.CreatedAt);
    }
}
