using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AutomationProposalConfiguration : IEntityTypeConfiguration<AutomationProposal>
{
    public void Configure(EntityTypeBuilder<AutomationProposal> builder)
    {
        builder.ToTable("AutomationProposals");

        builder.HasKey(ap => ap.Id);

        builder.Property(ap => ap.Id)
            .ValueGeneratedNever();

        builder.Property(ap => ap.SourceType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ap => ap.SourceReferenceId)
            .HasMaxLength(100);

        builder.Property(ap => ap.BoardId)
            .HasMaxLength(100);

        builder.Property(ap => ap.RequestedByUserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ap => ap.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ap => ap.RiskLevel)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ap => ap.Summary)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(ap => ap.DiffPreview);

        builder.Property(ap => ap.ValidationIssues);

        builder.Property(ap => ap.ExpiresAt)
            .IsRequired();

        builder.Property(ap => ap.DecidedAt);

        builder.Property(ap => ap.DecidedByUserId)
            .HasMaxLength(100);

        builder.Property(ap => ap.AppliedAt);

        builder.Property(ap => ap.FailureReason)
            .HasMaxLength(1000);

        builder.Property(ap => ap.CorrelationId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ap => ap.CreatedAt)
            .IsRequired();

        builder.Property(ap => ap.UpdatedAt)
            .IsRequired();

        builder.HasMany(ap => ap.Operations)
            .WithOne(o => o.Proposal)
            .HasForeignKey(o => o.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ap => ap.Status);
        builder.HasIndex(ap => ap.RequestedByUserId);
        builder.HasIndex(ap => ap.BoardId);
        builder.HasIndex(ap => ap.CorrelationId);
        builder.HasIndex(ap => ap.ExpiresAt);
    }
}
