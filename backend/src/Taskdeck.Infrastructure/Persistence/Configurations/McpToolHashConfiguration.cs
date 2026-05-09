using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class McpToolHashConfiguration : IEntityTypeConfiguration<McpToolHash>
{
    public void Configure(EntityTypeBuilder<McpToolHash> builder)
    {
        builder.ToTable("McpToolHashes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.ToolName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.DefinitionHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.IsApproved)
            .IsRequired();

        builder.Property(e => e.ApprovedAt);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        // Unique index: one hash record per user per tool name
        builder.HasIndex(e => new { e.UserId, e.ToolName })
            .IsUnique();
    }
}
