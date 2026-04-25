using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProposalRevisionConfiguration : IEntityTypeConfiguration<ProposalRevision>
{
    public void Configure(EntityTypeBuilder<ProposalRevision> builder)
    {
        builder.ToTable("ProposalRevisions");

        builder.HasKey(pr => pr.Id);

        builder.Property(pr => pr.Id)
            .ValueGeneratedNever();

        builder.Property(pr => pr.ProposalId)
            .IsRequired();

        builder.Property(pr => pr.RevisionNumber)
            .IsRequired();

        builder.Property(pr => pr.EditorUserId)
            .IsRequired();

        builder.Property(pr => pr.RevisedPayload)
            .IsRequired();

        builder.Property(pr => pr.RevisedAt)
            .IsRequired();

        builder.Property(pr => pr.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(pr => pr.CreatedAt)
            .IsRequired();

        builder.Property(pr => pr.UpdatedAt)
            .IsRequired();

        // Unique constraint: one revision number per proposal
        builder.HasIndex(pr => new { pr.ProposalId, pr.RevisionNumber })
            .IsUnique();

        builder.HasIndex(pr => pr.ProposalId);
        builder.HasIndex(pr => pr.EditorUserId);
    }
}
