using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AutomationProposalOperationConfiguration : IEntityTypeConfiguration<AutomationProposalOperation>
{
    public void Configure(EntityTypeBuilder<AutomationProposalOperation> builder)
    {
        builder.ToTable("AutomationProposalOperations");

        builder.HasKey(apo => apo.Id);

        builder.Property(apo => apo.Id)
            .ValueGeneratedNever();

        builder.Property(apo => apo.ProposalId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(apo => apo.Sequence)
            .IsRequired();

        builder.Property(apo => apo.ActionType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(apo => apo.TargetType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(apo => apo.TargetId)
            .HasMaxLength(100);

        builder.Property(apo => apo.Parameters)
            .IsRequired();

        builder.Property(apo => apo.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(apo => apo.ExpectedVersion)
            .HasMaxLength(100);

        builder.Property(apo => apo.CreatedAt)
            .IsRequired();

        builder.Property(apo => apo.UpdatedAt)
            .IsRequired();

        builder.HasIndex(apo => apo.ProposalId);
        builder.HasIndex(apo => new { apo.ProposalId, apo.Sequence });
        builder.HasIndex(apo => apo.IdempotencyKey)
            .IsUnique();
    }
}
