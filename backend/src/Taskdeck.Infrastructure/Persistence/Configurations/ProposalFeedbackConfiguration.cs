using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProposalFeedbackConfiguration : IEntityTypeConfiguration<ProposalFeedback>
{
    public void Configure(EntityTypeBuilder<ProposalFeedback> builder)
    {
        builder.ToTable("ProposalFeedbacks");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .ValueGeneratedNever();

        builder.Property(f => f.ProposalId)
            .IsRequired();

        builder.Property(f => f.ReportedByUserId)
            .IsRequired();

        builder.Property(f => f.Reason)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(f => f.ReportedAt)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        // One negative-feedback signal per user per proposal -- the structural idempotency
        // guarantee that makes a double-click / retry a benign no-op (UnitOfWork maps the
        // racing duplicate to a benign success).
        builder.HasIndex(f => new { f.ProposalId, f.ReportedByUserId })
            .IsUnique();

        // Cohort range scans (the future reported-bad-rate metric, #1142) read by user + period.
        // A standalone Reason index is intentionally deferred until a category picker ships.
        builder.HasIndex(f => new { f.ReportedByUserId, f.CreatedAt });

        builder.HasOne<AutomationProposal>()
            .WithMany()
            .HasForeignKey(f => f.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
