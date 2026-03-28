using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AgentRunEventConfiguration : IEntityTypeConfiguration<AgentRunEvent>
{
    public void Configure(EntityTypeBuilder<AgentRunEvent> builder)
    {
        builder.ToTable("AgentRunEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.RunId)
            .IsRequired();

        builder.Property(e => e.SequenceNumber)
            .IsRequired();

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Payload)
            .HasMaxLength(16000);

        builder.Property(e => e.Timestamp)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.RunId, e.SequenceNumber }).IsUnique();
    }
}
