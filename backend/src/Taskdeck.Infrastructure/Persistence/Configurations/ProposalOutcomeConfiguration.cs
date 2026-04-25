using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProposalOutcomeConfiguration : IEntityTypeConfiguration<ProposalOutcome>
{
    public void Configure(EntityTypeBuilder<ProposalOutcome> builder)
    {
        builder.ToTable("ProposalOutcomes");

        builder.HasKey(po => po.Id);

        builder.Property(po => po.Id)
            .ValueGeneratedNever();

        builder.Property(po => po.ProposalId)
            .IsRequired();

        builder.Property(po => po.DecidedByUserId)
            .IsRequired();

        builder.Property(po => po.Decision)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(po => po.OutcomeType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(po => po.DecidedAt)
            .IsRequired();

        builder.Property(po => po.DecisionLatencySeconds)
            .IsRequired();

        builder.Property(po => po.FieldCount)
            .IsRequired();

        builder.Property(po => po.EditedFieldCount)
            .IsRequired();

        builder.Property(po => po.SourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(po => po.RiskLevel)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(po => po.ModelId)
            .HasMaxLength(100);

        builder.Property(po => po.AverageFieldConfidence);

        builder.Property(po => po.CreatedAt)
            .IsRequired();

        builder.Property(po => po.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(po => po.ProposalId);
        builder.HasIndex(po => po.DecidedByUserId);
        builder.HasIndex(po => po.Decision);
        builder.HasIndex(po => po.CreatedAt);

        builder.HasOne(po => po.Proposal)
            .WithMany(p => p.Outcomes)
            .HasForeignKey(po => po.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
