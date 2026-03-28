using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class LlmUsageRecordConfiguration : IEntityTypeConfiguration<LlmUsageRecord>
{
    public void Configure(EntityTypeBuilder<LlmUsageRecord> builder)
    {
        builder.ToTable("LlmUsageRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.Surface)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(r => r.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.InputTokens)
            .IsRequired();

        builder.Property(r => r.OutputTokens)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // Indexes for quota queries
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => new { r.UserId, r.CreatedAt });
        builder.HasIndex(r => new { r.Surface, r.CreatedAt });
    }
}
