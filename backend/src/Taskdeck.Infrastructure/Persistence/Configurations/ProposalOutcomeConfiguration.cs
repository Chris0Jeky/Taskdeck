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

        builder.Property(po => po.OutcomeType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(po => po.DecidedByUserId)
            .IsRequired();

        builder.Property(po => po.DecidedAt)
            .IsRequired();

        builder.Property(po => po.CreatedAt)
            .IsRequired();

        builder.Property(po => po.UpdatedAt)
            .IsRequired();

        builder.HasIndex(po => po.ProposalId);
        builder.HasIndex(po => po.DecidedByUserId);
    }
}
