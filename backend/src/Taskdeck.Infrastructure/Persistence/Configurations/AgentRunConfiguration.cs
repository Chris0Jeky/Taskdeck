using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns");

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.Id)
            .ValueGeneratedNever();

        builder.Property(ar => ar.AgentProfileId)
            .IsRequired();

        builder.Property(ar => ar.UserId)
            .IsRequired();

        builder.Property(ar => ar.BoardId);

        builder.Property(ar => ar.TriggerType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(ar => ar.Objective)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(ar => ar.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ar => ar.Summary)
            .HasMaxLength(4000);

        builder.Property(ar => ar.FailureReason)
            .HasMaxLength(4000);

        builder.Property(ar => ar.ProposalId);

        builder.Property(ar => ar.StepsExecuted)
            .IsRequired();

        builder.Property(ar => ar.TokensUsed)
            .IsRequired();

        builder.Property(ar => ar.ApproxCostUsd)
            .HasColumnType("decimal(10, 6)");

        builder.Property(ar => ar.StartedAt)
            .IsRequired();

        builder.Property(ar => ar.CompletedAt);

        builder.Property(ar => ar.CreatedAt)
            .IsRequired();

        builder.Property(ar => ar.UpdatedAt)
            .IsRequired();

        builder.HasOne<AgentProfile>()
            .WithMany()
            .HasForeignKey(ar => ar.AgentProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ar => ar.Events)
            .WithOne(e => e.Run)
            .HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ar => ar.AgentProfileId);
        builder.HasIndex(ar => ar.UserId);
        builder.HasIndex(ar => ar.Status);
        builder.HasIndex(ar => ar.CreatedAt);
    }
}
