using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class LlmRequestConfiguration : IEntityTypeConfiguration<LlmRequest>
{
    public void Configure(EntityTypeBuilder<LlmRequest> builder)
    {
        builder.ToTable("LlmRequests");

        builder.HasKey(lr => lr.Id);

        builder.Property(lr => lr.UserId)
            .IsRequired();

        builder.Property(lr => lr.BoardId);

        builder.Property(lr => lr.RequestType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(lr => lr.Payload)
            .IsRequired();

        builder.Property(lr => lr.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(lr => lr.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(lr => lr.ProcessedAt);

        builder.Property(lr => lr.RetryCount)
            .IsRequired();

        builder.Property(lr => lr.CreatedAt)
            .IsRequired();

        builder.Property(lr => lr.UpdatedAt)
            .IsRequired();

        // Relationships
        builder.HasOne(lr => lr.User)
            .WithMany()
            .HasForeignKey(lr => lr.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(lr => lr.Board)
            .WithMany()
            .HasForeignKey(lr => lr.BoardId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes for queue processing
        builder.HasIndex(lr => lr.Status);
        builder.HasIndex(lr => lr.CreatedAt);
        builder.HasIndex(lr => new { lr.UserId, lr.Status });
    }
}
