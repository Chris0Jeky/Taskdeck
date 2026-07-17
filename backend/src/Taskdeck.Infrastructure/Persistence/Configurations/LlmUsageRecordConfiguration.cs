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

        // Reservation lifecycle (issue #1313). Status is always written explicitly by the app (the
        // constructor and RecordUsageAsync produce Committed; the reservation flow produces Reserved),
        // so it is NOT configured as a store-generated default here — that would make EF omit a
        // CLR-default Reserved (0) value on insert and silently store the DB default instead. Existing
        // rows are backfilled to Committed by the migration's one-time AddColumn defaultValue.
        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(r => r.ExpiresAt);

        // Indexes for quota queries
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => new { r.UserId, r.CreatedAt });
        builder.HasIndex(r => new { r.Surface, r.CreatedAt });
        // Sweep of expired reservations and the "live reserved" filter both probe (Status, ExpiresAt).
        builder.HasIndex(r => new { r.Status, r.ExpiresAt });
    }
}
