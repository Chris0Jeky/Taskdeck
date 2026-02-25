using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class OutboundWebhookDeliveryConfiguration : IEntityTypeConfiguration<OutboundWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<OutboundWebhookDelivery> builder)
    {
        builder.ToTable("OutboundWebhookDeliveries");

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.SubscriptionId)
            .IsRequired();

        builder.Property(delivery => delivery.BoardId)
            .IsRequired();

        builder.Property(delivery => delivery.EventType)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(delivery => delivery.Payload)
            .IsRequired();

        builder.Property(delivery => delivery.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WebhookDeliveryStatus.Pending);

        builder.Property(delivery => delivery.AttemptCount)
            .IsRequired();

        builder.Property(delivery => delivery.NextAttemptAt)
            .IsRequired();

        builder.Property(delivery => delivery.LastErrorMessage)
            .HasMaxLength(1000);

        builder.Property(delivery => delivery.CreatedAt)
            .IsRequired();

        builder.Property(delivery => delivery.UpdatedAt)
            .IsRequired();

        builder.HasOne(delivery => delivery.Subscription)
            .WithMany()
            .HasForeignKey(delivery => delivery.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAt });
        builder.HasIndex(delivery => delivery.SubscriptionId);
        builder.HasIndex(delivery => delivery.CreatedAt);
    }
}
