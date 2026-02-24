using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class OutboundWebhookSubscriptionConfiguration : IEntityTypeConfiguration<OutboundWebhookSubscription>
{
    public void Configure(EntityTypeBuilder<OutboundWebhookSubscription> builder)
    {
        builder.ToTable("OutboundWebhookSubscriptions");

        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.BoardId)
            .IsRequired();

        builder.Property(subscription => subscription.CreatedByUserId)
            .IsRequired();

        builder.Property(subscription => subscription.EndpointUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(subscription => subscription.SigningSecret)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(subscription => subscription.EventFilters)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(subscription => subscription.IsActive)
            .IsRequired();

        builder.Property(subscription => subscription.CreatedAt)
            .IsRequired();

        builder.Property(subscription => subscription.UpdatedAt)
            .IsRequired();

        builder.HasOne(subscription => subscription.Board)
            .WithMany()
            .HasForeignKey(subscription => subscription.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(subscription => subscription.CreatedByUser)
            .WithMany()
            .HasForeignKey(subscription => subscription.CreatedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(subscription => new { subscription.BoardId, subscription.IsActive });
        builder.HasIndex(subscription => subscription.CreatedAt);
    }
}
