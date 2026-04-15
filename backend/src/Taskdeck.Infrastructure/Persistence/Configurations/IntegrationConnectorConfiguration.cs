using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class IntegrationConnectorConfiguration : IEntityTypeConfiguration<IntegrationConnector>
{
    public void Configure(EntityTypeBuilder<IntegrationConnector> builder)
    {
        builder.ToTable("IntegrationConnectors");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.ConnectorType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(c => c.Direction)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(c => c.Configuration)
            .HasMaxLength(4000);

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => new { c.UserId, c.Status });
        builder.HasIndex(c => c.CreatedAt);
    }
}
