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
    }
}
