using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

        // SQLite stores DateTime as TEXT without timezone info. EF Core materializes
        // these as DateTimeKind.Unspecified, which causes incorrect comparisons with
        // DateTime.UtcNow. These converters normalize the Kind to UTC on read.
        // See: https://github.com/Chris0Jeky/Taskdeck/issues/1191
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        builder.Property(ap => ap.ExpiresAt)
            .IsRequired()
            .HasConversion(utcConverter);

        builder.Property(ap => ap.DecidedAt)
            .HasConversion(nullableUtcConverter);

        builder.Property(ap => ap.DecidedByUserId)
            .HasMaxLength(100);

        builder.Property(ap => ap.AppliedAt)
            .HasConversion(nullableUtcConverter);

        builder.Property(ap => ap.FailureReason)
            .HasMaxLength(1000);

        builder.Property(ap => ap.CorrelationId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ap => ap.CreatedAt)
            .IsRequired();

        builder.Property(ap => ap.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasMany(ap => ap.Operations)
            .WithOne(o => o.Proposal)
            .HasForeignKey(o => o.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ap => ap.Revisions)
            .WithOne(r => r.Proposal)
            .HasForeignKey(r => r.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ap => ap.Outcomes)
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
