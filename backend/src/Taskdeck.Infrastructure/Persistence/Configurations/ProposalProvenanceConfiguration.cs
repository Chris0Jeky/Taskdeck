using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ProposalProvenanceConfiguration : IEntityTypeConfiguration<ProposalProvenance>
{
    public void Configure(EntityTypeBuilder<ProposalProvenance> builder)
    {
        builder.ToTable("ProposalProvenances");

        builder.HasKey(pp => pp.Id);

        builder.Property(pp => pp.Id)
            .ValueGeneratedNever();

        builder.Property(pp => pp.ProposalId)
            .IsRequired();

        builder.Property(pp => pp.CorrelationId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pp => pp.ModelId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pp => pp.TotalTokens)
            .IsRequired();

        builder.Property(pp => pp.CreatedAt)
            .IsRequired();

        builder.Property(pp => pp.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne<AutomationProposal>()
            .WithOne()
            .HasForeignKey<ProposalProvenance>(pp => pp.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pp => pp.ProposalId)
            .IsUnique();

        builder.HasMany(pp => pp.Fields)
            .WithOne()
            .HasForeignKey(f => f.ProposalProvenanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
