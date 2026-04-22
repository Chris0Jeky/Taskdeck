using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class ConnectorEventConfiguration : IEntityTypeConfiguration<ConnectorEvent>
{
    public void Configure(EntityTypeBuilder<ConnectorEvent> builder)
    {
        builder.ToTable("ConnectorEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ConnectorId)
            .IsRequired();

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Payload)
            .HasMaxLength(1000);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.HasIndex(e => e.ConnectorId);
        builder.HasIndex(e => new { e.ConnectorId, e.CreatedAt });

        builder.HasOne<IntegrationConnector>()
            .WithMany()
            .HasForeignKey(e => e.ConnectorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
