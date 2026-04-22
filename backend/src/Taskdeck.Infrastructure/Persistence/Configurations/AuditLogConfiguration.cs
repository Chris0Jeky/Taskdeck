using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(al => al.Id);

        builder.Property(al => al.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(al => al.EntityId)
            .IsRequired();

        builder.Property(al => al.Action)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(al => al.UserId);

        builder.Property(al => al.Changes)
            .HasMaxLength(4000);

        builder.Property(al => al.Timestamp)
            .IsRequired();

        builder.Property(al => al.CreatedAt)
            .IsRequired();

        builder.Property(al => al.UpdatedAt)
            .IsRequired();

        // Relationships
        builder.HasOne(al => al.User)
            .WithMany()
            .HasForeignKey(al => al.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes for efficient querying
        builder.HasIndex(al => new { al.EntityType, al.EntityId });
        builder.HasIndex(al => al.Timestamp);
        builder.HasIndex(al => al.UserId);

        // PERF-10: composite indexes for common history/audit queries.
        // (UserId, Timestamp) accelerates per-user audit reads ordered by recency
        // (AuditLogRepository.GetByUserAsync).
        builder.HasIndex(al => new { al.UserId, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_UserId_Timestamp");

        // Note: AuditLog has no BoardId column; board scope is resolved via
        // EntityId (see AuditLogRepository.GetByBoardAsync). A composite on
        // (EntityId, Timestamp) accelerates that ORDER BY Timestamp DESC pattern
        // and serves as the board-scoped analogue named for in PERF-10.
        builder.HasIndex(al => new { al.EntityId, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_EntityId_Timestamp");
    }
}
