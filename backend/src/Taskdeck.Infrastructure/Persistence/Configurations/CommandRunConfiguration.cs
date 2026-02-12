using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CommandRunConfiguration : IEntityTypeConfiguration<CommandRun>
{
    public void Configure(EntityTypeBuilder<CommandRun> builder)
    {
        builder.ToTable("CommandRuns");

        builder.HasKey(cr => cr.Id);

        builder.Property(cr => cr.Id)
            .ValueGeneratedNever();

        builder.Property(cr => cr.TemplateName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cr => cr.RequestedByUserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cr => cr.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(cr => cr.StartedAt);

        builder.Property(cr => cr.CompletedAt);

        builder.Property(cr => cr.ExitCode);

        builder.Property(cr => cr.Truncated)
            .IsRequired();

        builder.Property(cr => cr.CorrelationId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cr => cr.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(cr => cr.OutputPreview)
            .HasMaxLength(1000);

        builder.Property(cr => cr.CreatedAt)
            .IsRequired();

        builder.Property(cr => cr.UpdatedAt)
            .IsRequired();

        builder.HasMany(cr => cr.Logs)
            .WithOne(l => l.CommandRun)
            .HasForeignKey(l => l.CommandRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(cr => cr.Status);
        builder.HasIndex(cr => cr.RequestedByUserId);
        builder.HasIndex(cr => cr.CorrelationId);
        builder.HasIndex(cr => cr.CreatedAt);
    }
}
