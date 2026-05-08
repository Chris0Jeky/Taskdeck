using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class McpToolHashConfiguration : IEntityTypeConfiguration<McpToolHash>
{
    public void Configure(EntityTypeBuilder<McpToolHash> builder)
    {
        builder.ToTable("McpToolHashes");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .IsRequired();

        builder.Property(h => h.ToolName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(h => h.DefinitionHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(h => h.IsApproved)
            .IsRequired();

        builder.Property(h => h.ApprovedAt);

        builder.Property(h => h.CreatedAt)
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .IsRequired();

        builder.HasIndex(h => new { h.UserId, h.ToolName })
            .IsUnique();
    }
}
